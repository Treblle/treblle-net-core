using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Treblle.Net.Core.Tests;

/// <summary>
/// The SDK must be invisible to the application it monitors.
/// </summary>
/// <remarks>
/// These are the only tests here whose failure would be an incident rather than a gap in a
/// dashboard. The middleware replaces <c>HttpContext.Response.Body</c> with a buffering stream
/// and copies the buffer back to the real stream afterwards, so every one of these asserts the
/// same underlying property: whatever Treblle does on the way past, the caller's request and
/// response come out the other side exactly as the application wrote them.
/// </remarks>
public class HostPipelineTests
{
    [Fact]
    public async Task Response_body_reaches_the_client_unchanged()
    {
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapGet("/orders", async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"id":1,"total":99.5}""");
            }));

        var response = await host.Client.GetAsync("/orders");

        Assert.Equal("""{"id":1,"total":99.5}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_large_response_body_is_not_truncated_or_duplicated()
    {
        // Buffer-and-copy-back bugs tend to surface only past a single write, so this spans
        // many writes and asserts on an exact byte count.
        var chunk = new string('x', 8 * 1024);

        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapGet("/big", async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("\"");
                for (var i = 0; i < 64; i++)
                {
                    await context.Response.WriteAsync(chunk);
                }
                await context.Response.WriteAsync("\"");
            }));

        var body = await host.Client.GetStringAsync("/big");

        Assert.Equal((64 * chunk.Length) + 2, body.Length);
    }

    [Fact]
    public async Task Status_code_and_response_headers_are_unchanged()
    {
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapGet("/teapot", context =>
            {
                context.Response.StatusCode = 418;
                context.Response.ContentType = "application/json";
                context.Response.Headers["X-Custom"] = "kept";
                return context.Response.WriteAsync("""{"brewing":false}""");
            }));

        var response = await host.Client.GetAsync("/teapot");

        Assert.Equal(418, (int)response.StatusCode);
        Assert.Equal("kept", response.Headers.GetValues("X-Custom").Single());
        Assert.Equal("""{"brewing":false}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_endpoint_can_still_read_the_request_body()
    {
        // The middleware calls EnableBuffering and reads the body for the payload. If it fails
        // to rewind the stream, model binding downstream sees an empty body — the application
        // starts rejecting every valid POST as malformed.
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapPost("/echo", async context =>
            {
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(body);
            }));

        var content = new StringContent("""{"name":"ada"}""", Encoding.UTF8, "application/json");
        var response = await host.Client.PostAsync("/echo", content);

        Assert.Equal("""{"name":"ada"}""", await response.Content.ReadAsStringAsync());

        var payload = await host.WaitForPayloadJsonAsync();
        Assert.Equal("ada", payload.StringAt("data.request.body.name"));
    }

    [Fact]
    public async Task Server_sent_events_reach_the_client_before_the_response_completes()
    {
        // The capture stream is supposed to detect text/event-stream on the first write and
        // hand the real body back. If it ever buffers instead, this test does not fail with a
        // wrong value — the client blocks until the timeout, because the first event cannot
        // arrive until the endpoint finishes.
        var firstEventRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapGet("/events", async context =>
            {
                context.Response.ContentType = "text/event-stream";

                await context.Response.WriteAsync("data: first\n\n");
                await context.Response.Body.FlushAsync();

                // Only completes once the client has actually received the chunk above.
                await firstEventRead.Task.WaitAsync(TimeSpan.FromSeconds(10));

                await context.Response.WriteAsync("data: second\n\n");
                await context.Response.Body.FlushAsync();
            }));

        using var response = await host.Client.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("data: first", Encoding.UTF8.GetString(buffer, 0, read));

        firstEventRead.SetResult();
    }

    [Fact]
    public async Task An_endpoint_exception_still_reaches_the_host()
    {
        // The middleware catches to attach the error to the payload, and must rethrow. If it
        // ever swallowed, a failing endpoint would return 200 with an empty body.
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapGet("/boom", _ => throw new InvalidOperationException("kaboom")));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Client.GetAsync("/boom"));

        Assert.Equal("kaboom", thrown.Message);

        var payload = await host.WaitForPayloadJsonAsync();
        var error = payload.Path("data.errors")!.Value[0];

        Assert.Equal("InvalidOperationException", error.GetProperty("type").GetString());
        Assert.Equal("kaboom", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_failing_ingress_never_affects_the_response()
    {
        // Telemetry delivery failing is routine — DNS, proxies, outages. It must stay entirely
        // on the sender's side of the fence.
        await using var host = await TreblleTestHost.StartAsync(
            endpoints => endpoints.MapGet("/orders", context =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"ok":true}""");
            }),
            configureOptions: options => options.CustomIngressEndpoint = "https://127.0.0.1:1/ingress");

        var response = await host.Client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"ok":true}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_misconfigured_sdk_passes_every_request_straight_through()
    {
        // No credentials, so ValidateOptions disables the SDK. A configuration mistake must
        // never take the host API down, and must not quietly send anything either.
        await using var host = await TreblleTestHost.StartAsync(
            endpoints => endpoints.MapGet("/orders", context =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"ok":true}""");
            }),
            withCredentials: false);

        var response = await host.Client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"ok":true}""", await response.Content.ReadAsStringAsync());
        await host.AssertNoPayloadAsync();
    }

    [Fact]
    public async Task A_response_with_no_body_is_handled()
    {
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapDelete("/orders/1", context =>
            {
                context.Response.StatusCode = 204;
                return Task.CompletedTask;
            }));

        var response = await host.Client.DeleteAsync("/orders/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());

        var payload = await host.WaitForPayloadJsonAsync();
        Assert.Equal(204, payload.Path("data.response.code")!.Value.GetInt32());
    }
}
