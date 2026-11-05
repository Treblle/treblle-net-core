using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Treblle.Net.Core.Masking;
using Treblle.Runtime.Masking;

namespace Treblle.Net.Core.Tests;

/// <summary>
/// Masking is the SDK's only safeguard against shipping a customer's secrets to a third party.
/// </summary>
/// <remarks>
/// It fails open by construction: every path through <see cref="JsonMasker"/> returns the input
/// unchanged when anything goes wrong, which is the right call for availability and the wrong
/// one for silence. A rule that stops matching does not raise an error anywhere — the password
/// simply starts arriving in the clear. So these tests assert on the masked output rather than
/// on the masker in isolation.
/// </remarks>
public class MaskingTests
{
    private static (MaskingRules Rules, MaskerFactory Factory) BuildMasking(Dictionary<string, string>? overrides = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTreblle("test-sdk-token", "test-api-key", overrides);

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<MaskingRules>(), provider.GetRequiredService<MaskerFactory>());
    }

    private static string Mask(string json, Dictionary<string, string>? overrides = null)
    {
        var (rules, factory) = BuildMasking(overrides);

        return json.Mask(rules, factory)!;
    }

    private static JsonElement MaskToJson(string json, Dictionary<string, string>? overrides = null)
        => JsonDocument.Parse(Mask(json, overrides)).RootElement;

    [Fact]
    public void Default_sensitive_fields_are_masked_in_the_request_body()
    {
        var masked = MaskToJson("""
            {"data":{"request":{"body":{"username":"ada","password":"hunter2","ssn":"123-45-6789"}}}}
            """);

        Assert.Equal("ada", masked.StringAt("data.request.body.username"));
        Assert.Equal("*******", masked.StringAt("data.request.body.password"));
        Assert.DoesNotContain("hunter2", masked.ToString());
        Assert.DoesNotContain("123-45-6789", masked.ToString());
    }

    [Fact]
    public void Authorization_headers_are_masked()
    {
        var masked = MaskToJson("""
            {"data":{"request":{"headers":{"authorization":"Bearer sk-live-abc123","x-api-key":"secret-key","accept":"application/json"}}}}
            """);

        Assert.DoesNotContain("sk-live-abc123", masked.ToString());
        Assert.DoesNotContain("secret-key", masked.ToString());
        Assert.Equal("application/json", masked.StringAt("data.request.headers.accept"));
    }

    [Fact]
    public void Sensitive_fields_are_masked_at_any_depth()
    {
        var masked = MaskToJson("""
            {"data":{"request":{"body":{"account":{"owner":{"password":"hunter2"}}}}}}
            """);

        Assert.DoesNotContain("hunter2", masked.ToString());
    }

    [Fact]
    public void Sensitive_fields_are_masked_inside_arrays()
    {
        var masked = MaskToJson("""
            {"data":{"request":{"body":{"users":[{"name":"ada","password":"hunter2"},{"name":"bob","password":"letmein"}]}}}}
            """);

        Assert.DoesNotContain("hunter2", masked.ToString());
        Assert.DoesNotContain("letmein", masked.ToString());
        Assert.Contains("ada", masked.ToString());
    }

    [Fact]
    public void Case_differences_in_field_names_do_not_defeat_masking()
    {
        var masked = MaskToJson("""
            {"data":{"request":{"body":{"PassWord":"hunter2","Secret":"s3cr3t"}}}}
            """);

        Assert.DoesNotContain("hunter2", masked.ToString());
        Assert.DoesNotContain("s3cr3t", masked.ToString());
    }

    [Fact]
    public void Developer_supplied_metadata_is_passed_through_unmasked()
    {
        // Deliberate, and matching the Laravel SDK: data.metadata is written by the
        // application, not by an API client, so a key named "email" there is a deployment
        // label rather than a user's address. Easy to regress into redacting it.
        var masked = MaskToJson("""
            {"data":{"metadata":{"email":"ops@example.com","secret":"build-42"},"request":{"body":{"secret":"s3cr3t"}}}}
            """);

        Assert.Equal("ops@example.com", masked.StringAt("data.metadata.email"));
        Assert.Equal("build-42", masked.StringAt("data.metadata.secret"));
        Assert.DoesNotContain("s3cr3t", masked.ToString());
    }

    [Fact]
    public void A_metadata_key_elsewhere_in_the_payload_is_still_masked()
    {
        // The carve-out is full-path equality on "data.metadata" exactly, so a user-supplied
        // object that happens to be called metadata must not inherit the exemption.
        var masked = MaskToJson("""
            {"data":{"request":{"body":{"metadata":{"password":"hunter2"}}}}}
            """);

        Assert.DoesNotContain("hunter2", masked.ToString());
    }

    [Fact]
    public void Masking_preserves_every_other_field_and_the_json_shape()
    {
        var masked = MaskToJson("""
            {"api_key":"key","sdk":"net-core","data":{"request":{"method":"POST","body":{"password":"hunter2","amount":42,"active":true,"nothing":null}},"response":{"code":200}}}
            """);

        Assert.Equal("net-core", masked.GetProperty("sdk").GetString());
        Assert.Equal("POST", masked.StringAt("data.request.method"));
        Assert.Equal(200, masked.Path("data.response.code")!.Value.GetInt32());
        Assert.Equal(42, masked.Path("data.request.body.amount")!.Value.GetInt32());
        Assert.True(masked.Path("data.request.body.active")!.Value.GetBoolean());
    }

    [Fact]
    public void Custom_rules_are_merged_over_the_defaults()
    {
        var masked = MaskToJson(
            """{"data":{"request":{"body":{"internal_token":"abc123","password":"hunter2"}}}}""",
            new Dictionary<string, string> { ["internal_token"] = nameof(DefaultStringMasker) });

        Assert.DoesNotContain("abc123", masked.ToString());
        Assert.DoesNotContain("hunter2", masked.ToString());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"unterminated": """)]
    public void Unparseable_input_is_returned_unchanged_rather_than_null(string json)
    {
        // TreblleService does `jsonPayload.Mask(...) ?? string.Empty` — a null here would POST
        // an empty body to the ingress and lose the request with a 2xx, invisibly.
        var (rules, factory) = BuildMasking();

        Assert.Equal(json, json.Mask(rules, factory));
    }

    [Fact]
    public async Task Disabling_masking_sends_the_body_verbatim()
    {
        await using var host = await TreblleTestHost.StartAsync(
            endpoints => endpoints.MapPost("/login", context =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"ok":true}""");
            }),
            configureOptions: options => options.DisableMasking = true);

        var content = new StringContent("""{"password":"hunter2"}""", Encoding.UTF8, "application/json");
        await host.Client.PostAsync("/login", content);

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("hunter2", payload.StringAt("data.request.body.password"));
    }

    [Fact]
    public async Task A_password_posted_to_a_live_endpoint_never_leaves_the_process()
    {
        // The end-to-end version of the first test: through the real middleware, the real
        // factory, the real serializer and the real sender, as the ingress would receive it.
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapPost("/login", context =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"token":"issued"}""");
            }));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/login")
        {
            Content = new StringContent("""{"email":"ada@example.com","password":"hunter2"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", "Bearer sk-live-abc123");

        await host.Client.SendAsync(request);

        var payload = await host.WaitForPayloadAsync();

        Assert.DoesNotContain("hunter2", payload);
        Assert.DoesNotContain("sk-live-abc123", payload);
    }
}

/// <summary>
/// The card masker is the one masker with real branching, and the branch it gets wrong is the
/// one that returns the input untouched.
/// </summary>
public class CreditCardMaskerTests
{
    private static string Mask(string input) => new CreditCardMasker().Mask(input);

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4111-1111-1111-1111")]
    public void A_sixteen_digit_card_is_reduced_to_its_last_four(string card)
    {
        Assert.Equal("****-****-****-1111", Mask(card));
    }

    [Theory]
    [InlineData("378282246310005")]          // 15-digit Amex
    [InlineData("4111111111111111111")]      // 19-digit PAN
    [InlineData("4111-1111-1111-1111-111")]  // 19 digits, grouped
    [InlineData("card is 4111111111111111 thanks")]
    public void A_card_number_that_is_not_exactly_sixteen_digits_is_still_masked(string input)
    {
        // The masker is reached because a field was configured as a card field. Whether the
        // value parses as a 16-digit PAN is not a reason to pass it through in the clear.
        var digits = new string(input.Where(char.IsDigit).ToArray());
        var masked = Mask(input);

        Assert.NotEqual(input, masked);
        Assert.DoesNotContain(digits, masked);
    }

    [Fact]
    public void An_empty_value_is_handled()
    {
        Assert.Equal(string.Empty, Mask(string.Empty));
    }
}
