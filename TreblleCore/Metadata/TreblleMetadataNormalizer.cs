using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Treblle.Net.Core;

/// <summary>
/// Reduces arbitrary metadata values to the flat, scalar shape the Treblle payload schema
/// allows, and enforces the schema's limits on the metadata object as a whole.
/// </summary>
/// <remarks>
/// The schema constrains <c>data.metadata</c> to at most <see cref="MaxEntries"/> properties,
/// keys of at most <see cref="MaxKeyLength"/> characters, and values that are a string of at
/// most <see cref="MaxStringLength"/> characters, a number, or a boolean. Objects, arrays and
/// nulls are not permitted, so a complex value is rendered as its JSON text rather than nested.
/// <para>
/// Flattening also keeps the metadata subtree monomorphic, which matters independently: the
/// payload is written by a source-generated serializer with no reflection fallback, and a value
/// whose runtime type is not declared with <c>[JsonSerializable]</c> throws
/// <see cref="NotSupportedException"/>. That throw surfaces on the background sender, costing
/// the whole request's telemetry rather than just the offending key.
/// </para>
/// </remarks>
internal static class TreblleMetadataNormalizer
{
    /// <summary>Schema limit on <c>data.metadata</c> property count.</summary>
    internal const int MaxEntries = 20;

    /// <summary>Schema limit on a metadata key's length.</summary>
    internal const int MaxKeyLength = 64;

    /// <summary>Schema limit on a metadata string value's length.</summary>
    internal const int MaxStringLength = 128;

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
    /// Normalizes <paramref name="value"/> and writes it to <paramref name="target"/> if the
    /// result is representable. Keys that are blank, over-long, or beyond the entry cap are
    /// dropped, as are values that normalize to null — the schema permits no null. An existing
    /// key can always be updated, so the cap bounds distinct keys rather than writes.
    /// </summary>
    internal static void Write(IDictionary<string, object?> target, string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            return;
        }

        if (target.Count >= MaxEntries && !target.ContainsKey(key))
        {
            return;
        }

        var normalized = Normalize(value);
        if (normalized is null)
        {
            return;
        }

        target[key] = normalized;
    }

    /// <summary>
    /// Returns null, a string of at most <see cref="MaxStringLength"/> characters, a numeric
    /// type, or a bool — the scalar set the schema allows and the serialization context declares.
    /// </summary>
    internal static object? Normalize(object? value)
    {
        switch (value)
        {
            case null:
                return null;

            // Already safe, and the overwhelmingly common cases.
            case string s:
                return Truncate(s);
            case bool b:
                return b;
            case long l:
                return l;

            // JSON has no NaN or Infinity, so a non-finite value would make the payload
            // unserializable. Render it as text instead of losing the whole request.
            case double d:
                return double.IsFinite(d) ? d : (object)d.ToString(CultureInfo.InvariantCulture);
            case float f:
                return float.IsFinite(f) ? f : (object)f.ToString(CultureInfo.InvariantCulture);

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

            // Kept at its own precision; widening to double would be lossy.
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
                return Truncate(uri.ToString());
            case Enum e:
                return Truncate(e.ToString());

            case JsonElement je:
                return FromJsonElement(je);

            default:
                return ToJsonText(value);
        }
    }

    /// <summary>
    /// Unwraps a <see cref="JsonElement"/> to a scalar. Objects and arrays become their JSON
    /// text, since the schema does not allow nesting.
    /// </summary>
    private static object? FromJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return Truncate(element.GetString() ?? string.Empty);
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : (object)element.GetDouble();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return Truncate(element.GetRawText());
        }
    }

    /// <summary>
    /// Renders a complex value as JSON text. Text rather than a nested node: the schema allows
    /// only scalars, and text also detaches the value from the original object, which matters
    /// because the payload is serialized on a background task after the request scope is gone.
    /// </summary>
    private static object? ToJsonText(object value)
    {
        try
        {
            return Truncate(JsonSerializer.Serialize(value, value.GetType(), SerializerOptions));
        }
        catch
        {
            // Unserializable (cycles, disposed state, a throwing getter). Fall back to the
            // type's own rendering rather than dropping the key silently.
            try
            {
                var text = value.ToString();
                return text is null ? null : Truncate(text);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Caps a string at <see cref="MaxStringLength"/>, marking the cut so a truncated value is
    /// not mistaken for a complete one.
    /// </summary>
    private static string Truncate(string value)
    {
        return value.Length <= MaxStringLength
            ? value
            : value.Substring(0, MaxStringLength - 1) + "…";
    }
}
