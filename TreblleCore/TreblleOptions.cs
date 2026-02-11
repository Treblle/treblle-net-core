using System.Collections.Generic;

namespace Treblle.Net.Core;

public sealed class TreblleOptions
{
    /// <summary>
    /// SDK Token for authentication (sent as api_key in payload)
    /// </summary>
    public string SdkToken { get; set; } = string.Empty;

    /// <summary>
    /// API Key for project identification (sent as project_id in payload)
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
}
