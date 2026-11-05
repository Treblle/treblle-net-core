using System.Collections.Generic;

namespace Treblle.Net.Core;

public sealed class TreblleOptions
{
    /// <summary>
    /// SDK Token for authentication (sent as sdk_token in payload)
    /// </summary>
    public string SdkToken { get; set; } = string.Empty;

    /// <summary>
    /// API Key for project identification (sent as api_key in payload)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public Dictionary<string, string>? FieldsToMaskPairedWithMaskers { get; set; }
    
    /// <summary>
    /// When true, skips all data masking operations. This significantly improves performance
    /// and reduces memory usage for high-volume scenarios where masking is not needed.
    /// </summary>
    public bool DisableMasking { get; set; } = false;
    
    /// <summary>
    /// When true, enables debug logging for troubleshooting purposes.
    /// This includes validation errors, middleware status, and other diagnostic information.
    /// </summary>
    public bool DebugMode { get; set; } = false;
    
    /// <summary>
    /// Array of path patterns to exclude from Treblle tracking.
    /// Supports wildcards: "/admin/*", "/health", "/metrics/*", etc.
    /// When null or empty, all endpoints are tracked by default.
    /// Case-insensitive matching is used.
    /// </summary>
    public string[]? ExcludedPaths { get; set; } = null;

    /// <summary>
    /// Custom ingress endpoint URL for sending telemetry data.
    /// When set, this endpoint will be used instead of the default Treblle endpoints.
    /// Must be a valid HTTPS URL. Trailing slashes will be removed automatically.
    /// Example: "https://ingress-eu.treblle.com"
    /// Can also be set via TREBLLE_CUSTOM_INGRESS_ENDPOINT environment variable.
    /// </summary>
    public string? CustomIngressEndpoint { get; set; }

    /// <summary>
    /// Static metadata added to every payload, such as deployment region or build version.
    /// Per-request values set through <see cref="ITreblleMetadata"/> are merged over these,
    /// so on a key collision the per-request value wins.
    /// Not bound from configuration — set it in the AddTreblle options delegate.
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; set; }

    /// <summary>
    /// How many payloads may be in flight to the ingress at once. Each worker holds one
    /// outstanding POST, so sustained throughput is roughly this divided by the round trip time
    /// — at a 150 ms RTT, the default of 8 sustains about 50 payloads a second. Raise it for a
    /// high-volume API; the SDK warns when the queue starts dropping. Clamped to 1-64.
    /// </summary>
    public int SenderConcurrency { get; set; } = 8;

    /// <summary>
    /// Upper bound on the memory held by queued payloads waiting to be sent. The queue is
    /// bounded by bytes rather than by item count alone, because item count says nothing about
    /// memory when a single payload can approach 5 MB. Oldest payloads are discarded first.
    /// </summary>
    public long MaxQueuedBytes { get; set; } = 64L * 1024L * 1024L;

    /// <summary>
    /// Whether the HTTP client sending telemetry honours the system proxy. True by default:
    /// an environment with mandatory egress proxying otherwise loses every payload silently.
    /// Set to false to skip proxy resolution in environments that have none.
    /// </summary>
    public bool UseSystemProxy { get; set; } = true;

    /// <summary>
    /// False when the SDK could not be configured — missing credentials, or a custom ingress
    /// endpoint that is not a valid HTTPS URL. In that state the middleware passes every
    /// request straight through: a configuration problem must never take the host API down.
    /// The reason is logged once at startup by TreblleStartupLogger.
    /// </summary>
    internal bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Why <see cref="IsEnabled"/> is false.
    /// </summary>
    internal string? DisabledReason { get; set; }
}
