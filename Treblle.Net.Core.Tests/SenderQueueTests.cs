using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Treblle.Net.Core.Tests;

/// <summary>
/// The outbound queue has to keep accepting work for the lifetime of the process.
/// </summary>
/// <remarks>
/// The queue is bounded by bytes, and the byte counter is adjusted in three places: on write,
/// when a worker reads, and when the channel evicts. Miss one, or get a sign wrong, and the
/// counter ratchets upward until <c>TryEnqueue</c> refuses everything forever. The application
/// keeps serving traffic and the SDK reports nothing, with no error anywhere — the single
/// worst failure mode this component has, and invisible without a test like this.
/// </remarks>
public class SenderQueueTests
{
    [Fact]
    public async Task The_queue_keeps_accepting_payloads_over_many_requests()
    {
        await using var host = await TreblleTestHost.StartAsync(endpoints =>
            endpoints.MapGet("/orders", context =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"ok":true}""");
            }));

        // Sequential, waiting for each payload, so a drifting counter shows up as a timeout
        // on a specific iteration rather than as a flaky count at the end.
        for (var i = 0; i < 25; i++)
        {
            await host.Client.GetAsync("/orders");
            await host.WaitForPayloadAsync();
        }

        Assert.Equal(25, host.Payloads.Count);
    }
}
