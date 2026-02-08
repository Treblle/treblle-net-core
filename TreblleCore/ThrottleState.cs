using System;
using System.Threading;

namespace Treblle.Net.Core;

/// <summary>
/// Lock-free state machine for handling rate limiting (HTTP 429) responses.
/// Uses Interlocked operations for thread safety with minimal memory footprint.
/// </summary>
internal sealed class ThrottleState
{
    private const int StateNormal = 0;
    private const int StateBackingOff = 1;
    private const int StateProbing = 2;

    private const long InitialBackoffMs = 1000;      // 1 second
    private const long MaxBackoffMs = 60000;         // 60 seconds
    private const double BackoffMultiplier = 2.0;
    private const double JitterFactor = 0.2;         // 20% jitter
    private const int SuccessesRequiredForRecovery = 2;

    private static readonly Random JitterRandom = new();

    // State fields - using long for Interlocked compatibility
    private long _state = StateNormal;
    private long _backoffUntilTicks;
    private long _currentBackoffMs = InitialBackoffMs;
    private long _consecutiveSuccesses;

    /// <summary>
    /// Checks if payloads should be throttled (dropped) based on current state.
    /// This is an O(1) operation with no allocations.
    /// </summary>
    /// <returns>True if payload should be dropped, false if it should be sent.</returns>
    public bool ShouldThrottle()
    {
        var state = Interlocked.Read(ref _state);

        if (state == StateNormal)
        {
            return false;
        }

        if (state == StateBackingOff)
        {
            var backoffUntil = Interlocked.Read(ref _backoffUntilTicks);
            var now = DateTime.UtcNow.Ticks;

            if (now >= backoffUntil)
            {
                // Backoff expired, transition to probing
                Interlocked.CompareExchange(ref _state, StateProbing, StateBackingOff);
                return false; // Allow this request through as probe
            }

            return true; // Still in backoff, throttle
        }

        // StateProbing - allow limited traffic through
        return false;
    }

    /// <summary>
    /// Records a 429 (Too Many Requests) response from the API.
    /// </summary>
    /// <param name="retryAfterSeconds">Optional Retry-After header value in seconds.</param>
    public void RecordThrottleResponse(int? retryAfterSeconds)
    {
        long backoffMs;

        if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
        {
            // Use server-specified retry-after duration
            backoffMs = Math.Min(retryAfterSeconds.Value * 1000L, MaxBackoffMs);
        }
        else
        {
            // Use exponential backoff with jitter
            var currentBackoff = Interlocked.Read(ref _currentBackoffMs);
            var jitter = 1.0 + (JitterRandom.NextDouble() * JitterFactor * 2 - JitterFactor);
            backoffMs = (long)(currentBackoff * jitter);

            // Double the backoff for next time (up to max)
            var nextBackoff = Math.Min((long)(currentBackoff * BackoffMultiplier), MaxBackoffMs);
            Interlocked.Exchange(ref _currentBackoffMs, nextBackoff);
        }

        var backoffUntil = DateTime.UtcNow.AddMilliseconds(backoffMs).Ticks;
        Interlocked.Exchange(ref _backoffUntilTicks, backoffUntil);
        Interlocked.Exchange(ref _consecutiveSuccesses, 0);
        Interlocked.Exchange(ref _state, StateBackingOff);
    }

    /// <summary>
    /// Records a successful API response. Multiple consecutive successes
    /// during probing state will transition back to normal.
    /// </summary>
    public void RecordSuccess()
    {
        var state = Interlocked.Read(ref _state);

        if (state == StateNormal)
        {
            return; // Already normal, nothing to do
        }

        if (state == StateProbing)
        {
            var successes = Interlocked.Increment(ref _consecutiveSuccesses);

            if (successes >= SuccessesRequiredForRecovery)
            {
                // Recovered - reset to normal state
                Interlocked.Exchange(ref _currentBackoffMs, InitialBackoffMs);
                Interlocked.Exchange(ref _consecutiveSuccesses, 0);
                Interlocked.Exchange(ref _state, StateNormal);
            }
        }
    }
}
