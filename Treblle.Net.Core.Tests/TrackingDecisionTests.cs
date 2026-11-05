using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Treblle.Net.Core.Tests;

/// <summary>
/// Which requests get sent at all.
/// </summary>
/// <remarks>
/// Kept deliberately thin. The full content-type and static-asset matrix is not worth pinning
/// down — it is tuning, and tests over tuning turn into a chore the next time it is tuned.
/// What is worth holding is the handful of decisions a customer would notice: an exclusion
/// they configured must actually exclude, and an ordinary API call must not be filtered out.
/// </remarks>
public class TrackingDecisionTests
{
    private static Task<TreblleTestHost> HostWith(
        Action<TreblleOptions>? configureOptions = null,
        params string[] routes)
    {
        return TreblleTestHost.StartAsync(
            endpoints =>
            {
                foreach (var route in routes)
                {
                    endpoints.MapGet(route, context =>
                    {
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync("""{"ok":true}""");
                    });

                    endpoints.MapPost(route, context =>
                    {
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync("""{"ok":true}""");
                    });
                }
            },
            configureOptions);
    }

    [Fact]
    public async Task An_ordinary_api_call_is_tracked()
    {
        await using var host = await HostWith(routes: "/api/orders");

        await host.Client.GetAsync("/api/orders");

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("/api/orders", payload.StringAt("data.request.route_path"));
    }

    [Fact]
    public async Task A_configured_exclusion_is_honoured()
    {
        await using var host = await HostWith(
            options => options.ExcludedPaths = new[] { "/internal/*" },
            "/internal/health", "/api/orders");

        await host.Client.GetAsync("/internal/health");

        await host.AssertNoPayloadAsync();
    }

    [Fact]
    public async Task An_exclusion_does_not_catch_neighbouring_paths()
    {
        // A pattern that over-matches silently stops reporting a live endpoint, which looks
        // from the dashboard exactly like traffic that stopped arriving.
        await using var host = await HostWith(
            options => options.ExcludedPaths = new[] { "/internal/*" },
            "/internal/health", "/api/orders");

        await host.Client.GetAsync("/api/orders");

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("/api/orders", payload.StringAt("data.request.route_path"));
    }

    [Fact]
    public async Task Swagger_and_other_static_tooling_paths_are_skipped_by_default()
    {
        await using var host = await HostWith(routes: "/swagger/index.html");

        await host.Client.GetAsync("/swagger/index.html");

        await host.AssertNoPayloadAsync();
    }

    [Fact]
    public async Task Soap_requests_are_skipped()
    {
        // The payload schema has nowhere to put a SOAP envelope, so these are dropped rather
        // than sent as unparseable noise.
        await using var host = await HostWith(routes: "/service");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/service")
        {
            Content = new StringContent("<soap:Envelope />", Encoding.UTF8, "application/soap+xml")
        };
        request.Headers.Add("SOAPAction", "urn:GetOrders");

        await host.Client.SendAsync(request);

        await host.AssertNoPayloadAsync();
    }
}
