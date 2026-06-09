using System.Text.Json.Serialization;

namespace Treblle.Net.Core;

public sealed class QueryEntry
{
    [JsonPropertyName("sql")]
    public string Sql { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public double Time { get; set; }
}
