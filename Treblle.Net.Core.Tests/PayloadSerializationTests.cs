using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Treblle.Net.Core.Tests;

/// <summary>
/// Guards the payload against failing to serialize at all.
/// </summary>
/// <remarks>
/// <see cref="TreblleJsonContext"/> is source-generated and has no reflection fallback, so a
/// value whose runtime type is not declared with <c>[JsonSerializable]</c> throws
/// <see cref="NotSupportedException"/>. The throw happens on the background sender, after the
/// response has already gone out, so there is nothing to see from the application's side: the
/// request simply never appears on the dashboard. One undeclared type costs the whole payload,
/// not just the offending field, which is what makes this worth testing rather than trusting.
/// </remarks>
public class PayloadSerializationTests
{
    private static string Serialize(TrebllePayload payload)
        => JsonSerializer.Serialize(payload, TreblleJsonContext.Default.TrebllePayload);

    private static TrebllePayload FullyPopulatedPayload()
    {
        var payload = new TrebllePayload
        {
            Sdk = "net-core",
            Version = TreblleConstants.PayloadVersion,
            ApiKey = "api-key",
            SdkToken = "sdk-token",
        };

        payload.Data.Server.Ip = "10.0.0.1";
        payload.Data.Server.Timezone = "UTC";
        payload.Data.Server.Protocol = "HTTP/1.1";
        payload.Data.Server.Os.Name = "Unix";

        payload.Data.Language.Name = "c#";
        payload.Data.Language.Version = "10";

        payload.Data.Request.Timestamp = "2026-11-05 12:00:00";
        payload.Data.Request.Url = "https://api.example.com/orders/1";
        payload.Data.Request.RoutePath = "/orders/{id}";
        payload.Data.Request.Method = "POST";
        payload.Data.Request.Headers = new Dictionary<string, object> { ["content-type"] = "application/json" };
        payload.Data.Request.Body = JsonSerializer.Deserialize<JsonElement>("""{"name":"ada"}""");

        payload.Data.Response.Code = 200;
        payload.Data.Response.Size = 42;
        payload.Data.Response.LoadTime = 12.5;
        payload.Data.Response.Headers = new Dictionary<string, object> { ["content-type"] = "application/json" };
        payload.Data.Response.Body = JsonSerializer.Deserialize<JsonElement>("""{"ok":true}""");

        payload.Data.Errors.Add(new Error { Source = "onException", Type = "InvalidOperationException", Message = "boom", Line = 7 });
        payload.Data.Queries.Add(new QueryEntry());

        return payload;
    }

    [Fact]
    public void A_fully_populated_payload_serializes()
    {
        var json = Serialize(FullyPopulatedPayload());

        using var document = JsonDocument.Parse(json);

        Assert.Equal("net-core", document.RootElement.GetProperty("sdk").GetString());
        Assert.Equal("/orders/{id}", document.RootElement.StringAt("data.request.route_path"));
    }

    public static TheoryData<object> SupportedMetadataValues() => new()
    {
        "a string",
        true,
        (long)42,
        42,
        (short)42,
        (byte)42,
        (sbyte)42,
        (ushort)42,
        (uint)42,
        (ulong)42,
        3.14d,
        3.14f,
        3.14m,
    };

    [Theory]
    [MemberData(nameof(SupportedMetadataValues))]
    public void Every_type_the_normalizer_emits_is_serializable(object value)
    {
        // The normalizer's contract is that it only ever yields types TreblleJsonContext
        // declares. If the two drift apart, this is where it shows up.
        var payload = FullyPopulatedPayload();
        TreblleMetadataNormalizer.Write(payload.Data.Metadata, "value", value);

        var json = Serialize(payload);

        Assert.Contains("\"value\"", json);
    }

    public static TheoryData<object> UnsupportedMetadataValues() => new()
    {
        Guid.NewGuid(),
        new DateTime(2026, 11, 5),
        new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero),
        TimeSpan.FromMinutes(5),
        new[] { 1, 2, 3 },
        new List<string> { "a", "b" },
        new Dictionary<string, string> { ["k"] = "v" },
        new Uri("https://example.com"),
        new { Anonymous = true },
    };

    [Theory]
    [MemberData(nameof(UnsupportedMetadataValues))]
    public void A_value_of_an_undeclared_type_is_flattened_rather_than_thrown_on(object value)
    {
        // Callers pass whatever they have. Anything the context cannot write must be reduced
        // to something it can, because the alternative is losing the entire request silently.
        var payload = FullyPopulatedPayload();
        TreblleMetadataNormalizer.Write(payload.Data.Metadata, "value", value);

        var json = Serialize(payload);

        using var document = JsonDocument.Parse(json);
        var written = document.RootElement.Path("data.metadata.value");

        Assert.NotNull(written);
        Assert.NotEqual(JsonValueKind.Object, written!.Value.ValueKind);
        Assert.NotEqual(JsonValueKind.Array, written.Value.ValueKind);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_number_does_not_make_the_payload_unserializable(double value)
    {
        // JSON has no NaN or Infinity; writing one raw throws and costs the payload.
        var payload = FullyPopulatedPayload();
        TreblleMetadataNormalizer.Write(payload.Data.Metadata, "value", value);

        var json = Serialize(payload);

        // Rendered as text. A bare NaN or Infinity token is not valid JSON, so the ingress
        // would reject the whole payload.
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.String, document.RootElement.Path("data.metadata.value")!.Value.ValueKind);
    }

    [Fact]
    public void The_oversize_body_replacement_serializes()
    {
        // TreblleService swaps an oversized request body for this dictionary and serializes
        // again. An anonymous type here would throw — and only for payloads over 5 MB, which
        // is exactly the case nobody exercises by hand.
        var payload = FullyPopulatedPayload();
        payload.Data.Request.Body = new Dictionary<string, object>
        {
            ["__message"] = "Request data payload was bigger than 5MB"
        };

        var json = Serialize(payload);

        Assert.Contains("bigger than 5MB", json);
    }

    [Fact]
    public void Metadata_is_capped_at_twenty_entries()
    {
        var metadata = new Dictionary<string, object?>();

        for (var i = 0; i < TreblleMetadataNormalizer.MaxEntries + 10; i++)
        {
            TreblleMetadataNormalizer.Write(metadata, $"key{i}", i);
        }

        Assert.Equal(TreblleMetadataNormalizer.MaxEntries, metadata.Count);
    }

    [Fact]
    public void An_existing_key_can_still_be_updated_once_the_cap_is_reached()
    {
        var metadata = new Dictionary<string, object?>();

        for (var i = 0; i < TreblleMetadataNormalizer.MaxEntries; i++)
        {
            TreblleMetadataNormalizer.Write(metadata, $"key{i}", i);
        }

        TreblleMetadataNormalizer.Write(metadata, "key0", "updated");

        Assert.Equal(TreblleMetadataNormalizer.MaxEntries, metadata.Count);
        Assert.Equal("updated", metadata["key0"]);
    }

    [Fact]
    public void Over_long_keys_are_dropped_and_over_long_values_truncated()
    {
        var metadata = new Dictionary<string, object?>();

        TreblleMetadataNormalizer.Write(metadata, new string('k', TreblleMetadataNormalizer.MaxKeyLength + 1), "value");
        TreblleMetadataNormalizer.Write(metadata, "long", new string('v', TreblleMetadataNormalizer.MaxStringLength + 50));

        Assert.Single(metadata);
        Assert.Equal(TreblleMetadataNormalizer.MaxStringLength, ((string)metadata["long"]!).Length);
    }

    [Fact]
    public async Task Per_request_metadata_reaches_the_payload()
    {
        await using var host = await TreblleTestHost.StartAsync(
            endpoints => endpoints.MapGet("/orders", context =>
            {
                context.RequestServices.GetRequiredService<ITreblleMetadata>().Meta("tenant", "acme");

                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"ok":true}""");
            }),
            configureOptions: options => options.Metadata = new Dictionary<string, object?>
            {
                ["region"] = "eu-west-1",
                ["tenant"] = "overwritten"
            });

        await host.Client.GetAsync("/orders");

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("eu-west-1", payload.StringAt("data.metadata.region"));
        Assert.Equal("acme", payload.StringAt("data.metadata.tenant"));
    }

    [Fact]
    public async Task An_oversize_request_body_is_replaced_rather_than_sent()
    {
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapPost("/upload", context =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"ok":true}""");
            }));

        var oversize = "{\"blob\":\"" + new string('x', 6 * 1024 * 1024) + "\"}";
        var content = new StringContent(oversize, Encoding.UTF8, "application/json");

        await host.Client.PostAsync("/upload", content);

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("large_request", payload.StringAt("data.request.body.__type"));
    }
}
