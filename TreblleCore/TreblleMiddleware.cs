using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Treblle.Net.Core;

internal class TreblleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TreblleService _treblleService;
    private readonly TrebllePayloadFactory _trebllePayloadFactory;
    private readonly ILogger<TreblleMiddleware> _logger;
    private readonly Channel<TrebllePayload> _channel;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _backgroundTask;

    public TreblleMiddleware(
        RequestDelegate next,
        TreblleService treblleService,
        TrebllePayloadFactory trebllePayloadFactory,
        ILogger<TreblleMiddleware> logger)
    {
        _next = next;
        _treblleService = treblleService;
        _trebllePayloadFactory = trebllePayloadFactory;
        _logger = logger;

        // Bounded channel prevents unbounded memory growth
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<TrebllePayload>(options);

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
                        _logger.LogWarning(ex, "Failed to send payload to Treblle, discarding payload to prevent memory accumulation");
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
        _cancellationTokenSource.Cancel();
        _channel.Writer.Complete();
        
        try
        {
            _backgroundTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Timeout waiting for Treblle background task to complete");
        }
        
        _cancellationTokenSource.Dispose();
    }

    public async Task Invoke(HttpContext httpContext)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<TreblleAttribute>() is not null)
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
        ValueStopwatch stopwatch = default;
        MemoryStream? memoryStream = null;
        bool shouldCaptureResponse = true;
        
        try
        {
            httpContext.Request.EnableBuffering();

            stopwatch = ValueStopwatch.StartNew();

            // Check if we should capture response based on expected size
            const long maxResponseSize = 5 * 1024 * 1024; // 5MB
            if (httpContext.Response.ContentLength.HasValue && 
                httpContext.Response.ContentLength.Value > maxResponseSize)
            {
                shouldCaptureResponse = false;
                _logger.LogDebug("Skipping response capture for large response: {Size} bytes", 
                    httpContext.Response.ContentLength.Value);
            }

            if (shouldCaptureResponse)
            {
                memoryStream = new MemoryStream();
                httpContext.Response.Body = memoryStream;
            }

            await _next(httpContext);

            var elapsed = stopwatch.GetElapsedTime();

            var payload = await _trebllePayloadFactory.CreateAsync(
                httpContext,
                memoryStream,
                (long)elapsed.TotalMilliseconds);

            if (memoryStream != null)
            {
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalResponseBody);
            }

            _channel.Writer.TryWrite(payload);
        }
        finally
        {
            httpContext.Response.Body = originalResponseBody;
            memoryStream?.Dispose();
            var elapsed = stopwatch.GetElapsedTime();
            var elapsedMiliseconds = (long)elapsed.TotalMilliseconds;
            httpContext.Items.Add("elapsedMiliseconds", elapsedMiliseconds);
        }
    }
}