namespace Treblle.Net.Core;

public sealed class TreblleOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    public string? AdditionalFieldsToMask { get; set; }
}
