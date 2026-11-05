using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Treblle.Net.Core;

/// <summary>
/// Owns the outbound payload queue and the workers that drain it.
/// </summary>
/// <remarks>
/// Previously the middleware owned a channel and a single <c>Task.Run</c> reader. That had four
/// problems, all fixed here:
/// <list type="bullet">
/// <item>One reader awaiting one POST at a time capped throughput at roughly 1/RTT. Measured
/// against the real ingress that is about 7 payloads a second, far below the request rate of a
/// busy API, so the queue filled and payloads were silently discarded.</item>
/// <item>The queue was bounded by item count only, so 3000 slots of large payloads could retain
/// gigabytes. It is now bounded by bytes as well.</item>
/// <item>ASP.NET Core never disposes middleware, so the reader task, its cancellation source and
/// everything queued were abandoned on shutdown — every deploy lost the backlog, and each host
/// built in a test run leaked another reader. This is a hosted service, so shutdown is real.</item>
/// <item>Drops were silent. They are now counted and reported.</item>
/// </list>
/// </remarks>
internal sealed class TreblleSender : IHostedService
{
    /// <summary>Secondary guard: the byte budget is the one that matters, this bounds small payloads.</summary>
    private const int MaxQueuedPayloads = 3000;

    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DropLogInterval = TimeSpan.FromSeconds(30);

    private readonly Channel<TrebllePayload> _channel;
    private readonly TreblleService _treblleService;
    private readonly TreblleOptions _options;
    private readonly ILogger<TreblleSender> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly int _concurrency;
    private readonly long _maxQueuedBytes;

    private Task[]? _workers;
    private long _queuedBytes;
    private long _dropped;
    private long _lastDropLogTicks;

    public TreblleSender(TreblleService treblleService, IOptions<TreblleOptions> options, ILogger<TreblleSender> logger)
    {
        _treblleService = treblleService;
        _options = options.Value;
        _logger = logger;
        _concurrency = Math.Clamp(_options.SenderConcurrency, 1, 64);
        _maxQueuedBytes = Math.Max(_options.MaxQueuedBytes, 1024L * 1024L);

        var channelOptions = new BoundedChannelOptions(MaxQueuedPayloads)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        };

        // Drop-oldest keeps the freshest telemetry. The callback is what makes the byte budget
        // exact: an evicted payload has to give its bytes back, or the counter drifts upward
        // until the queue refuses everything.
        _channel = Channel.CreateBounded<TrebllePayload>(channelOptions, OnPayloadDropped);
    }

    /// <summary>Total payloads discarded since startup, for diagnostics.</summary>
    internal long DroppedCount => Interlocked.Read(ref _dropped);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _workers = new Task[_concurrency];

        for (var i = 0; i < _concurrency; i++)
        {
            _workers[i] = Task.Run(() => RunWorkerAsync(_shutdown.Token));
        }

        if (_options.DebugMode)
        {
            _logger.LogDebug(
                "[TREBLLE]: Sender started with {Concurrency} workers, {Budget} MB queue budget",
                _concurrency, _maxQueuedBytes / (1024 * 1024));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Queues a payload. Never blocks and never throws: telemetry must not be able to fail or
    /// stall the request that produced it.
    /// </summary>
    internal bool TryEnqueue(TrebllePayload payload)
    {
        var size = payload.EstimatedSizeBytes;

        // Checked before the write, so a burst of large payloads is refused at the door rather
        // than evicting the entire queue one item at a time.
        if (Interlocked.Read(ref _queuedBytes) + size > _maxQueuedBytes)
        {
            RecordDrop();
            return false;
        }

        Interlocked.Add(ref _queuedBytes, size);

        if (_channel.Writer.TryWrite(payload))
        {
            return true;
        }

        // Writer completed (shutdown in progress).
        Interlocked.Add(ref _queuedBytes, -size);
        RecordDrop();
        return false;
    }

    private void OnPayloadDropped(TrebllePayload payload)
    {
        Interlocked.Add(ref _queuedBytes, -payload.EstimatedSizeBytes);
        RecordDrop();
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var payload))
                {
                    Interlocked.Add(ref _queuedBytes, -payload.EstimatedSizeBytes);

                    if (_treblleService.ShouldThrottle())
                    {
                        RecordDrop();

                        if (_options.DebugMode)
                        {
                            _logger.LogDebug("[TREBLLE]: Payload dropped - rate limited");
                        }

                        continue;
                    }

                    // SendPayloadAsync swallows its own failures; this is belt and braces so one
                    // bad payload can never take a worker down for the lifetime of the process.
                    try
                    {
                        await _treblleService.SendPayloadAsync(payload).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (_options.DebugMode)
                        {
                            _logger.LogDebug(ex, "[TREBLLE]: Failed to send payload, discarding it");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TREBLLE]: Sender worker stopped unexpectedly");
        }
    }

    private void RecordDrop()
    {
        var total = Interlocked.Increment(ref _dropped);

        // Rate-limited so an overloaded sender cannot also flood the log.
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastDropLogTicks);

        if (now - last < DropLogInterval.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastDropLogTicks, now, last) != last)
        {
            return;
        }

        _logger.LogWarning(
            "[TREBLLE]: {Total} payloads dropped since startup - the queue is full because the ingress is not keeping up with request volume. Raise TreblleOptions.SenderConcurrency (currently {Concurrency}) or MaxQueuedBytes (currently {Budget} MB).",
            total, _concurrency, _maxQueuedBytes / (1024 * 1024));
    }

    /// <summary>
    /// The periodic warning is rate limited, so the first one can fire early in a burst and
    /// report a number far below the eventual total. This reports the final figure once.
    /// </summary>
    private void ReportTotalDropped()
    {
        var dropped = Interlocked.Read(ref _dropped);

        if (dropped > 0)
        {
            _logger.LogWarning("[TREBLLE]: {Dropped} payloads were dropped in total during this process's lifetime.", dropped);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting, then give the workers a bounded window to flush what is already queued
        // rather than abandoning it the way the middleware-owned reader did.
        _channel.Writer.TryComplete();

        if (_workers is null)
        {
            return;
        }

        var allWorkers = Task.WhenAll(_workers);
        var completed = await Task.WhenAny(allWorkers, Task.Delay(ShutdownDrainTimeout)).ConfigureAwait(false);

        if (completed == allWorkers)
        {
            ReportTotalDropped();
            _shutdown.Cancel();
            _shutdown.Dispose();
            return;
        }

        ReportTotalDropped();

        var remaining = _channel.Reader.CanCount ? _channel.Reader.Count : -1;

        _logger.LogWarning(
            "[TREBLLE]: Shutdown drain timed out after {Seconds}s; {Remaining} queued payloads were not sent.",
            ShutdownDrainTimeout.TotalSeconds, remaining);

        // Cancelled but deliberately not disposed: workers are still in flight and hold this
        // token, and disposing it underneath them throws ObjectDisposedException. The process
        // is exiting anyway.
        _shutdown.Cancel();
    }
}
