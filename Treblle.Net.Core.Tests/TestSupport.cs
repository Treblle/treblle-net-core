using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Treblle.Net.Core;

namespace Treblle.Net.Core.Tests;

/// <summary>
/// Boots a real ASP.NET Core pipeline over <see cref="TestServer"/> with Treblle registered,
/// and captures the payloads the SDK would have POSTed to the ingress.
/// </summary>
/// <remarks>
/// The capture point is the "Treblle" named client's primary handler, which is as far
/// downstream as it is possible to get without a socket. Everything the payload passes through
/// on the way out — source-generated serialization, masking, the 5 MB replacement, gzip — has
/// already run by then, so a test that inspects a captured payload is asserting on the bytes
/// a customer's data would actually arrive as.
/// <para>
/// A fake <see cref="Microsoft.AspNetCore.Http.HttpContext"/> would be cheaper, and would also
/// miss the entire class of bug that matters most here: the middleware swaps
/// <c>Response.Body</c> for a buffering stream and copies it back afterwards, so whether the
/// caller receives their response at all is a property of the real pipeline.
/// </para>
/// </remarks>
internal sealed class TreblleTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly CapturingHandler _handler;

    private TreblleTestHost(IHost host, CapturingHandler handler)
    {
        _host = host;
        _handler = handler;
        Client = host.GetTestClient();
    }

    /// <summary>An HTTP client addressing the in-memory server.</summary>
    public HttpClient Client { get; }

    /// <summary>Payloads captured so far, as the JSON the ingress would have received.</summary>
    public IReadOnlyList<string> Payloads => _handler.Payloads;

    public static Task<TreblleTestHost> StartAsync(
        Action<IEndpointRouteBuilder> endpoints,
        Action<TreblleOptions>? configureOptions = null,
        bool useExceptionHandler = false,
        bool withCredentials = true,
        Action<IServiceCollection>? configureServices = null)
    {
        return StartWithPipelineAsync(
            app => app.UseEndpoints(endpoints),
            configureOptions,
            useExceptionHandler,
            withCredentials,
            configureServices);
    }

    /// <summary>
    /// For tests that need to place extra middleware between routing and the endpoints.
    /// </summary>
    public static async Task<TreblleTestHost> StartWithPipelineAsync(
        Action<IApplicationBuilder> configureAfterTreblle,
        Action<TreblleOptions>? configureOptions = null,
        bool useExceptionHandler = false,
        bool withCredentials = true,
        Action<IServiceCollection>? configureServices = null)
    {
        var handler = new CapturingHandler();

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();

                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

                    if (withCredentials)
                    {
                        services.AddTreblle("test-sdk-token", "test-api-key", options => configureOptions?.Invoke(options));
                    }
                    else
                    {
                        // No credentials anywhere, so ValidateOptions disables the SDK.
                        services.AddTreblle(options => configureOptions?.Invoke(options));
                    }

                    // Registered after AddTreblle so this primary handler is the one that wins.
                    services.AddHttpClient("Treblle").ConfigurePrimaryHttpMessageHandler(() => handler);

                    configureServices?.Invoke(services);
                });

                webHost.Configure(app =>
                {
                    // The order the README mandates: Treblle after routing, so the matched
                    // endpoint is available for route_path.
                    app.UseRouting();
                    app.UseTreblle(useExceptionHandler);
                    configureAfterTreblle(app);
                });
            })
            .StartAsync();

        return new TreblleTestHost(host, handler);
    }

    /// <summary>
    /// Waits for the background sender to deliver the next payload. The sender is a hosted
    /// service draining a channel on its own workers, so a test that reads
    /// <see cref="Payloads"/> immediately after a request is racing it.
    /// </summary>
    public Task<string> WaitForPayloadAsync(TimeSpan? timeout = null)
        => _handler.WaitForPayloadAsync(timeout ?? TimeSpan.FromSeconds(10));

    /// <summary>Waits for the next payload and parses it.</summary>
    public async Task<JsonElement> WaitForPayloadJsonAsync(TimeSpan? timeout = null)
        => JsonDocument.Parse(await WaitForPayloadAsync(timeout)).RootElement;

    /// <summary>
    /// Asserts that no payload arrives. Only meaningful with a real delay, so it is kept
    /// short and used sparingly.
    /// </summary>
    public async Task AssertNoPayloadAsync(TimeSpan? window = null)
    {
        var payload = await _handler.TryWaitForPayloadAsync(window ?? TimeSpan.FromMilliseconds(750));
        Assert.True(payload is null, $"Expected no payload to be sent, but got: {payload}");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        _handler.Dispose();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly List<string> _payloads = new();
        private readonly SemaphoreSlim _arrived = new(0);
        private readonly object _gate = new();

        public IReadOnlyList<string> Payloads
        {
            get { lock (_gate) { return _payloads.ToArray(); } }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);

            // Content-Encoding is only set when TryCompress actually compressed, so trusting
            // the header here is also a check that the sender sets it honestly.
            var json = request.Content.Headers.ContentEncoding.Contains("gzip")
                ? Decompress(bytes)
                : Encoding.UTF8.GetString(bytes);

            lock (_gate)
            {
                _payloads.Add(json);
            }

            _arrived.Release();

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public async Task<string> WaitForPayloadAsync(TimeSpan timeout)
        {
            var payload = await TryWaitForPayloadAsync(timeout);

            Assert.True(payload is not null, $"No Treblle payload was sent within {timeout.TotalSeconds:0.#}s.");

            return payload!;
        }

        public async Task<string?> TryWaitForPayloadAsync(TimeSpan timeout)
        {
            if (!await _arrived.WaitAsync(timeout))
            {
                return null;
            }

            lock (_gate)
            {
                return _payloads[^1];
            }
        }

        private static string Decompress(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);

            return reader.ReadToEnd();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _arrived.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>Navigation helpers, so assertions read as the payload path they are checking.</summary>
internal static class JsonElementExtensions
{
    /// <summary>Walks a dotted path, returning null when any segment is absent.</summary>
    public static JsonElement? Path(this JsonElement element, string dottedPath)
    {
        var current = element;

        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>The string at <paramref name="dottedPath"/>, or null when absent or JSON null.</summary>
    public static string? StringAt(this JsonElement element, string dottedPath)
    {
        var found = element.Path(dottedPath);

        return found is null || found.Value.ValueKind != JsonValueKind.String
            ? null
            : found.Value.GetString();
    }
}
