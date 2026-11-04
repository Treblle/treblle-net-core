using System;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Treblle.Net.Core;

/// <summary>
/// Reduces arbitrary metadata values to the closed set of runtime types that
/// <see cref="TreblleJsonContext"/> declares.
/// </summary>
/// <remarks>
/// Metadata values are user-supplied and land in a property typed <c>object</c>, which makes
/// them polymorphic. The payload is written by a source-generated serializer, and that has no
/// reflection fallback: a value whose runtime type is not declared with
/// <c>[JsonSerializable]</c> throws <see cref="NotSupportedException"/>. The throw would
/// surface on the background sender, where it is caught and logged — costing the whole
/// request's telemetry rather than just the offending key. Normalizing here keeps the
/// metadata subtree monomorphic, so that cannot happen whatever a caller passes.
/// </remarks>
internal static class TreblleMetadataNormalizer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Returns a value that is null, string, long, double, float, decimal, bool or
    /// <see cref="JsonElement"/> — every one of which the serialization context declares.
    /// </summary>
    internal static object? Normalize(object? value)
    {
        switch (value)
        {
            case null:
                return null;

            // Already safe, and the overwhelmingly common cases.
            case string s:
                return s;
            case bool b:
                return b;
            case long l:
                return l;
            case double d:
                return d;

            // Widened losslessly rather than registering every integral type.
            case int i:
                return (long)i;
            case short sh:
                return (long)sh;
            case sbyte sb:
                return (long)sb;
            case byte by:
                return (long)by;
            case ushort us:
                return (long)us;
            case uint ui:
                return (long)ui;
            case ulong ul:
                return ul <= long.MaxValue ? (long)ul : (object)ul.ToString(CultureInfo.InvariantCulture);

            // Kept at their own precision; widening these to double would be lossy.
            case float f:
                return f;
            case decimal m:
                return m;

            // Rendered culture-independently so the dashboard sees stable values.
            case char c:
                return c.ToString();
            case Guid g:
                return g.ToString();
            case DateTime dt:
                return dt.ToString("o", CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return dto.ToString("o", CultureInfo.InvariantCulture);
            case TimeSpan ts:
                return ts.ToString(null, CultureInfo.InvariantCulture);
            case Uri uri:
                return uri.ToString();
            case Enum e:
                return e.ToString();

            // default(JsonElement) is Undefined, and writing that throws.
            case JsonElement je:
                return je.ValueKind == JsonValueKind.Undefined ? null : (object)je;

            default:
                return ToJsonElement(value);
        }
    }

    /// <summary>
    /// Snapshots a complex value as a detached <see cref="JsonElement"/>. Detached matters:
    /// the payload is serialized on a background task after the request scope is disposed, so
    /// holding the original object could mean serializing something already disposed.
    /// </summary>
    private static object? ToJsonElement(object value)
    {
        try
        {
            return JsonSerializer.SerializeToElement(value, value.GetType(), SerializerOptions);
        }
        catch
        {
            // Unserializable (cycles, disposed state, a throwing getter). Fall back to the
            // type's own rendering rather than dropping the key silently.
            try
            {
                return value.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
