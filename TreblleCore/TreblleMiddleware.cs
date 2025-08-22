using Microsoft.AspNetCore.Http;
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
            await HandleRequestWithTreblleAsync(httpContext);
        }
        else
        {
            await _next(httpContext);
        }
    }

    private async Task HandleRequestWithTreblleAsync(HttpContext httpContext)
    {
        var originalResponseBody = httpContext.Response.Body;
        var stopwatch = Stopwatch.StartNew();
        MemoryStream? memoryStream = null;
        bool shouldCaptureResponse = true;
        
        try
        {
            httpContext.Request.EnableBuffering();

            // Check if we should capture response based on expected size
            const long maxResponseSize = 5 * 1024 * 1024; // 5MB
            if (httpContext.Response.ContentLength.HasValue && 
                httpContext.Response.ContentLength.Value > maxResponseSize)
            {
                shouldCaptureResponse = false;
                if (_options.DebugMode)
                {
                    _logger.LogDebug("[TREBLLE]: Skipping response capture for large response: {Size} bytes", 
                        httpContext.Response.ContentLength.Value);
                }
            }

            if (shouldCaptureResponse)
            {
                memoryStream = new MemoryStream();
                httpContext.Response.Body = memoryStream;
            }

            await _next(httpContext);

            if (memoryStream != null)
            {
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalResponseBody);
            }
        }
        catch (Exception ex)
        {
            // CRITICAL: Never let Treblle crash the user's request
            if (_options.DebugMode)
            {
                _logger.LogDebug(ex, "[TREBLLE]: Exception in middleware - continuing request without Treblle tracking");
            }
            
            // Ensure response stream is restored even if we fail
            httpContext.Response.Body = originalResponseBody;
            
            // Re-throw to preserve original application behavior
            throw;
        }
        finally
        {
            httpContext.Response.Body = originalResponseBody;
            stopwatch.Stop();
            var elapsedMiliseconds = stopwatch.ElapsedMilliseconds;
            httpContext.Items["elapsedMiliseconds"] = elapsedMiliseconds;

            // Create and send payload with accurate timing that includes response stream copy
            try
            {
                _logger.LogDebug("Treblle timing: {ElapsedMs}ms", elapsedMiliseconds);
                
                // Final check: only create payload if response should be tracked
                if (ApiRequestFilter.ShouldTrackResponse(httpContext))
                {
                    var payload = await _trebllePayloadFactory.CreateAsync(
                        httpContext,
                        memoryStream,
                        elapsedMiliseconds);

                    _channel.Writer.TryWrite(payload);
                    
                    if (_options.DebugMode)
                    {
                        var contentType = httpContext.Response.ContentType ?? "unknown";
                        _logger.LogDebug("[TREBLLE]: Tracked response with content type: {ContentType}", contentType);
                    }
                }
                else
                {
                    if (_options.DebugMode)
                    {
                        var contentType = httpContext.Response.ContentType ?? "unknown";
                        _logger.LogDebug("[TREBLLE]: Skipped response - non-API content type: {ContentType}", contentType);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_options.DebugMode)
                {
                    _logger.LogDebug(ex, "[TREBLLE]: Failed to create Treblle payload");
                }
            }

            // Dispose memory stream after payload creation
            memoryStream?.Dispose();
        }
    }
}