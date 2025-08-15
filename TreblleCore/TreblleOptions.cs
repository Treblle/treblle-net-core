using System.Collections.Generic;

namespace Treblle.Net.Core;

public sealed class TreblleOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    public Dictionary<string, string>? FieldsToMaskPairedWithMaskers { get; set; }
    
    /// <summary>
    /// When true, skips all data masking operations. This significantly improves performance
    /// and reduces memory usage for high-volume scenarios where masking is not needed.
    /// </summary>
    public bool DisableMasking { get; set; } = false;
}
