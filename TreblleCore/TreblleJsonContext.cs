using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Treblle.Net.Core;

/// <summary>
/// Source-generated JSON serialization context for Treblle payload types.
/// Provides compile-time serialization code generation for better performance.
/// </summary>
[JsonSerializable(typeof(TrebllePayload))]
[JsonSerializable(typeof(QueryEntry))]
[JsonSerializable(typeof(List<QueryEntry>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(object))] // For dynamic request/response bodies
[JsonSerializable(typeof(List<object>))] 
[JsonSerializable(typeof(string))] // For dictionary values
[JsonSerializable(typeof(Dictionary<string, object>))] // For headers
[JsonSerializable(typeof(Dictionary<string, string>))] // For form data and masking config
[JsonSerializable(typeof(long))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
internal partial class TreblleJsonContext : JsonSerializerContext
{
}
