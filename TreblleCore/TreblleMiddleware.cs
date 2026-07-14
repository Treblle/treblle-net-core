using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Treblle.Net.Core;

internal class TreblleMiddleware : IDisposable
{
    private readonly RequestDelegate _next;
    private readonly TreblleService _treblleService;
    private readonly TrebllePayloadFactory _trebllePayloadFactory;
    private readonly ILogger<TreblleMiddleware> _logger;
    private readonly TreblleOptions _options;
    private readonly Channel<TrebllePayload> _channel;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _backgroundTask;
    private bool _disposed = false;

    public TreblleMiddleware(
        RequestDelegate next,
        TreblleService treblleService,
        TrebllePayloadFactory trebllePayloadFactory,
        ILogger<TreblleMiddleware> logger,
        IOptions<TreblleOptions> options)
    {
        _next = next;
        _treblleService = treblleService;
        _trebllePayloadFactory = trebllePayloadFactory;
        _logger = logger;
        _options = options.Value;

        // Bounded channel prevents unbounded memory growth - increased capacity for high-volume scenarios
        var channelOptions = new BoundedChannelOptions(3000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<TrebllePayload>(channelOptions);

        _backgroundTask = Task.Run(() => SendPayloadAsync(_cancellationTokenSource.Token));
    }

    private async Task SendPayloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var payload))
                {
                    // Check if we're being rate limited - drop payload silently if so
                    if (_treblleService.ShouldThrottle())
                    {
                        if (_options.DebugMode)
                        {
                            _logger.LogDebug("[TREBLLE]: Payload dropped - rate limited");
                        }
                        continue; // Drop payload and process next
                    }

                    try
                    {
                        await _treblleService.SendPayloadAsync(payload);
                    }
                    catch (Exception ex)
                    {
                        // Log and discard failed payload immediately - no retry to prevent memory buildup
                        if (_options.DebugMode)
                        {
                            _logger.LogDebug(ex, "[TREBLLE]: Failed to send payload to Treblle, discarding payload to prevent memory accumulation");
                        }
                        // Payload is automatically discarded as we don't re-queue it
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Treblle background payload sender");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                try
                {
                    _cancellationTokenSource.Cancel();
                    _channel.Writer.Complete();

                    // Wait for background task to complete with timeout
                    if (!_backgroundTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        if (_options.DebugMode)
                        {
                            _logger.LogDebug("[TREBLLE]: Background task did not complete within timeout");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_options.DebugMode)
                    {
                        _logger.LogDebug(ex, "[TREBLLE]: Exception during middleware disposal");
                    }
                }
                finally
                {
                    _cancellationTokenSource.Dispose();
                }
            }

            _disposed = true;
        }
    }

    public async Task Invoke(HttpContext httpContext)
    {
        var treblleAttribute = httpContext.GetEndpoint()?.Metadata.GetMetadata<TreblleAttribute>();
        var requestPath = httpContext.Request.Path.Value ?? "/";

        // Determine if this endpoint should be tracked
        bool shouldTrack;

        if (treblleAttribute is not null)
        {
            // Explicit [Treblle] attribute always enables tracking (unless explicitly excluded)
            shouldTrack = !PathMatcher.ShouldExcludePath(requestPath, _options.ExcludedPaths);

            if (_options.DebugMode)
            {
                var endpoint = httpContext.GetEndpoint();
                var route = endpoint?.DisplayName ?? requestPath;
                if (shouldTrack)
                {
                    _logger.LogDebug("[TREBLLE]: Processing request with explicit [Treblle] attribute for {Route}", route);
                }
                else
                {
                    _logger.LogDebug("[TREBLLE]: Skipping request - [Treblle] attribute found but path {Route} is in ExcludedPaths", route);
                }
            }
        }
        else
        {
            // Auto-discovery mode: use intelligent filtering for API endpoints
            shouldTrack = ApiRequestFilter.ShouldTrackRequest(httpContext, _options.ExcludedPaths);

            if (_options.DebugMode)
            {
                var endpoint = httpContext.GetEndpoint();
                var route = endpoint?.DisplayName ?? requestPath;
                if (shouldTrack)
                {
                    _logger.LogDebug("[TREBLLE]: Auto-discovering endpoint {Route} - tracking enabled", route);
                }
                else
                {
                    _logger.LogDebug("[TREBLLE]: Auto-discovering endpoint {Route} - excluded by intelligent filtering", route);
                }
            }
        }

        if (shouldTrack)
        {
            await HandleRequestWithTreblleAsync(httpContext, treblleAttribute);
        }
        else
        {
            await _next(httpContext);
        }
    }

    private async Task HandleRequestWithTreblleAsync(HttpContext httpContext, TreblleAttribute? treblleAttribute)
    {
        var originalResponseBody = httpContext.Response.Body;
        var stopwatch = Stopwatch.StartNew();
        ResponseCaptureStream? captureStream = null;
        Exception? capturedException = null;

        // Capture route template before _next — UseExceptionHandler (registered after UseTreblle)
        // calls context.SetEndpoint(null) when handling errors, so GetEndpoint() returns null by the time
        // the finally block runs.
        var routeEndpoint = httpContext.GetEndpoint() as RouteEndpoint;
        var capturedRoutePath = routeEndpoint?.RoutePattern?.RawText is not null
            ? "/" + routeEndpoint.RoutePattern.RawText
            : null;

        try
        {
            TreblleQueryCollector.Initialize();

            httpContext.Request.EnableBuffering();

            // Skip response capture for responses larger than 5 MB — no point buffering them.
            const long maxResponseSize = 5 * 1024 * 1024;
            bool shouldCaptureResponse = !httpContext.Response.ContentLength.HasValue ||
                                         httpContext.Response.ContentLength.Value <= maxResponseSize;

            if (!shouldCaptureResponse && _options.DebugMode)
            {
                _logger.LogDebug("[TREBLLE]: Skipping response capture for large response: {Size} bytes",
                    httpContext.Response.ContentLength!.Value);
            }

            if (shouldCaptureResponse)
            {
                // ResponseCaptureStream intercepts the first write to check the actual Content-Type.
                // If it's text/event-stream the stream transparently passes all writes through to the
                // real response body so SSE chunks reach the client immediately. Otherwise it buffers
                // into a MemoryStream for Treblle payload construction, identical to previous behavior.
                captureStream = new ResponseCaptureStream(httpContext, originalResponseBody, _options, _logger);
                httpContext.Response.Body = captureStream;
            }

            await _next(httpContext);

            var captured = captureStream?.CapturedContent;
            if (captured != null)
            {
                captured.Position = 0;
                await captured.CopyToAsync(originalResponseBody);
            }
        }
        catch (Exception ex)
        {
            // Restore response stream and capture exception for payload — never suppress the throw
            httpContext.Response.Body = originalResponseBody;
            capturedException = ex;

            if (_options.DebugMode)
            {
                _logger.LogDebug(ex, "[TREBLLE]: Exception caught - will include in Treblle payload");
            }

            throw;
        }
        finally
        {
            httpContext.Response.Body = originalResponseBody;
            stopwatch.Stop();
            var elapsedMiliseconds = stopwatch.ElapsedMilliseconds;
            httpContext.Items["elapsedMiliseconds"] = elapsedMiliseconds;

            try
            {
                _logger.LogDebug("Treblle timing: {ElapsedMs}ms", elapsedMiliseconds);

                // Resolve the exception to attach to the payload:
                //   - capturedException: set when no exception handler absorbed the throw (propagated back to us)
                //   - IExceptionHandlerFeature.Error: set when UseExceptionHandler ran inside our scope
                var exception = capturedException
                    ?? httpContext.Features.Get<IExceptionHandlerFeature>()?.Error;

                if (capturedException != null || ApiRequestFilter.ShouldTrackResponse(httpContext))
                {
                    var payload = await _trebllePayloadFactory.CreateAsync(
                        httpContext,
                        // Response body was not captured when exception bypassed our stream swap
                        capturedException != null ? null : captureStream?.CapturedContent,
                        elapsedMiliseconds,
                        exception: exception,
                        treblleAttribute: treblleAttribute,
                        capturedRoutePath: capturedRoutePath);

                    _channel.Writer.TryWrite(payload);

                    if (_options.DebugMode)
                    {
                        if (exception != null)
                            _logger.LogDebug("[TREBLLE]: Tracked 500 with exception: {ExceptionType}", exception.GetType().Name);
                        else
                            _logger.LogDebug("[TREBLLE]: Tracked response with content type: {ContentType}", httpContext.Response.ContentType ?? "unknown");
                    }
                }
                else if (_options.DebugMode)
                {
                    _logger.LogDebug("[TREBLLE]: Skipped response - non-API content type: {ContentType}", httpContext.Response.ContentType ?? "unknown");
                }
            }
            catch (Exception ex)
            {
                if (_options.DebugMode)
                {
                    _logger.LogDebug(ex, "[TREBLLE]: Failed to create Treblle payload");
                }
            }

            captureStream?.Dispose();
        }
    }

    // Wraps the response body stream. On the first write it checks Response.ContentType:
    //   - text/event-stream → swaps Response.Body back to the real stream and passes through all writes
    //   - anything else     → buffers into a MemoryStream so the middleware can read the body for the payload
    private sealed class ResponseCaptureStream : Stream
    {
        private readonly HttpContext _httpContext;
        private readonly Stream _originalBody;
        private readonly TreblleOptions _options;
        private readonly ILogger _logger;

        private MemoryStream? _buffer;
        private bool _resolved;
        private bool _passThrough;

        public ResponseCaptureStream(HttpContext httpContext, Stream originalBody, TreblleOptions options, ILogger logger)
        {
            _httpContext = httpContext;
            _originalBody = originalBody;
            _options = options;
            _logger = logger;
        }

        // Returns the buffered content when in buffering mode, null when streaming or when nothing was written.
        public MemoryStream? CapturedContent => _passThrough ? null : _buffer;

        private Stream Resolve()
        {
            if (_resolved)
                return _passThrough ? _originalBody : _buffer!;

            _resolved = true;

            if (_httpContext.Response.ContentType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
            {
                _passThrough = true;
                // Restore the real stream so subsequent Response.Body accesses in the controller
                // loop go directly to the client without touching this wrapper again.
                _httpContext.Response.Body = _originalBody;

                if (_options.DebugMode)
                    _logger.LogDebug("[TREBLLE]: Skipping response capture for SSE response");

                return _originalBody;
            }

            _buffer = new MemoryStream();
            return _buffer;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => await Resolve().WriteAsync(buffer, offset, count, ct);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            => await Resolve().WriteAsync(buffer, ct);

        public override void Write(byte[] buffer, int offset, int count)
            => Resolve().Write(buffer, offset, count);

        public override Task FlushAsync(CancellationToken ct)
            => _passThrough ? _originalBody.FlushAsync(ct) : (_buffer?.FlushAsync(ct) ?? Task.CompletedTask);

        public override void Flush()
        {
            if (_passThrough) _originalBody.Flush();
            else _buffer?.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buffer?.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
