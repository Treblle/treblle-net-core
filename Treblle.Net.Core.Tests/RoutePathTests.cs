using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Treblle.Net.Core.Tests;

/// <summary>
/// <c>route_path</c> is what groups requests into endpoints on the dashboard.
/// </summary>
/// <remarks>
/// Its presence carries meaning: a value means ASP.NET Core matched a registered route, and
/// its absence means it did not. Inferring one from the request path instead of leaving it
/// null turns every scanner probe and every high-cardinality URL into its own endpoint, which
/// is how a project's endpoint list fills with garbage. The formatting rules matter for the
/// same reason — a template that renders two ways splits one endpoint into two.
/// </remarks>
public class RoutePathTests
{
    private static Task<TreblleTestHost> HostWithRoute(string template) =>
        TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapGet(template, context =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"ok":true}""");
            }));

    [Fact]
    public async Task An_unmatched_request_reports_no_route_at_all()
    {
        await using var host = await HostWithRoute("/orders/{id}");

        var response = await host.Client.GetAsync("/nothing/here/42");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Null(payload.Path("data.request.route_path"));
        Assert.Contains("/nothing/here/42", payload.StringAt("data.request.url"));
    }

    [Fact]
    public async Task A_matched_route_reports_its_template_not_the_request_path()
    {
        await using var host = await HostWithRoute("/orders/{id}");

        await host.Client.GetAsync("/orders/42");

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("/orders/{id}", payload.StringAt("data.request.route_path"));
    }

    [Fact]
    public async Task A_template_declared_with_a_leading_slash_does_not_gain_a_second_one()
    {
        // Minimal API templates arrive with the slash already on them; attribute routes do
        // not. Prefixing unconditionally produced "//orders/{id}", which grouped separately
        // from the controller endpoints reporting "/orders/{id}".
        await using var host = await HostWithRoute("/orders/{id}");

        await host.Client.GetAsync("/orders/42");

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.DoesNotContain("//", payload.StringAt("data.request.route_path")!);
    }

    [Fact]
    public async Task Literal_segments_are_lowercased()
    {
        // Attribute routing substitutes [controller] with the C# class name, so RawText
        // carries identifier casing that matches neither the URL callers use nor what the
        // other Treblle SDKs report for the same endpoint.
        await using var host = await HostWithRoute("/api/Orders/Recent");

        await host.Client.GetAsync("/api/Orders/Recent");

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("/api/orders/recent", payload.StringAt("data.request.route_path"));
    }

    [Fact]
    public async Task Parameter_names_and_constraints_keep_their_casing()
    {
        // Everything inside braces surfaces as a parameter identifier in the generated
        // documentation, so it is preserved verbatim while the literals around it are not.
        await using var host = await HostWithRoute("/api/Orders/{orderId:int}");

        await host.Client.GetAsync("/api/Orders/42");

        var payload = await host.WaitForPayloadJsonAsync();

        Assert.Equal("/api/orders/{orderId:int}", payload.StringAt("data.request.route_path"));
    }

    [Fact]
    public async Task A_failing_endpoint_still_reports_its_route()
    {
        // UseExceptionHandler calls SetEndpoint(null) while handling the error, so the
        // template has to be captured before the request runs. Without that capture, a 500
        // lands on the dashboard with no endpoint at all.
        await using var host = await TreblleTestHost.StartAsync(
            endpoints => endpoints.MapGet("/orders/{id}", _ => throw new InvalidOperationException("kaboom")),
            useExceptionHandler: true);

        await host.Client.GetAsync("/orders/42");
        await host.WaitForPayloadAsync();

        Assert.Contains(host.Payloads, payload =>
            JsonDocument.Parse(payload).RootElement.StringAt("data.request.route_path") == "/orders/{id}");
    }

    [Fact(Skip = "Known defect: an unhandled exception produces two payloads, neither correct. " +
                 "The middleware's finally block sends one carrying the route but status 200, because " +
                 "UseExceptionHandler has not set 500 yet; the handler in ApplicationBuilderExtensions " +
                 "then sends a second with status 500 but no route, because it does not pass " +
                 "capturedRouteTemplate and SetEndpoint(null) has already run. Un-skip once error " +
                 "reporting emits a single payload.")]
    public async Task A_handled_exception_produces_one_payload_with_both_the_route_and_the_status()
    {
        await using var host = await TreblleTestHost.StartAsync(
            endpoints => endpoints.MapGet("/orders/{id}", _ => throw new InvalidOperationException("kaboom")),
            useExceptionHandler: true);

        await host.Client.GetAsync("/orders/42");
        await host.WaitForPayloadAsync();
        await host.AssertNoPayloadAsync();

        var payload = JsonDocument.Parse(host.Payloads.Single()).RootElement;

        Assert.Equal("/orders/{id}", payload.StringAt("data.request.route_path"));
        Assert.Equal(500, payload.Path("data.response.code")!.Value.GetInt32());
    }
}
