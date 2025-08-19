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

    /// <summary>
    /// [Deprecated] Use SdkToken instead. This property is kept for backward compatibility.
    /// When set, it will automatically populate SdkToken if SdkToken is empty.
    /// </summary>
    [System.Obsolete("Use SdkToken instead. This property will be removed in a future version.")]
    public string LegacyApiKey 
    { 
        get => _legacyApiKey;
        set 
        {
            _legacyApiKey = value;
            // Auto-populate SdkToken if it's empty (for backward compatibility)
            if (string.IsNullOrEmpty(SdkToken) && !string.IsNullOrEmpty(value))
                SdkToken = value;
        }
    }
    private string _legacyApiKey = string.Empty;

    /// <summary>
    /// [Deprecated] Use ApiKey instead. This property is kept for backward compatibility.
    /// When set, it will automatically populate ApiKey if ApiKey is empty.
    /// </summary>
    [System.Obsolete("Use ApiKey instead. This property will be removed in a future version.")]
    public string ProjectId 
    { 
        get => _projectId;
        set 
        {
            _projectId = value;
            // Auto-populate ApiKey if it's empty (for backward compatibility)
            if (string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(value))
                ApiKey = value;
        }
    }
    private string _projectId = string.Empty;

    public Dictionary<string, string>? FieldsToMaskPairedWithMaskers { get; set; }
    
    /// <summary>
    /// When true, skips all data masking operations. This significantly improves performance
    /// and reduces memory usage for high-volume scenarios where masking is not needed.
    /// </summary>
    public bool DisableMasking { get; set; } = false;
}
