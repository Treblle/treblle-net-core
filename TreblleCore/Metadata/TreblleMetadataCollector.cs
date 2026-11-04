using System;
using System.Collections.Generic;

namespace Treblle.Net.Core;

/// <summary>
/// Per-request store behind <see cref="ITreblleMetadata"/>. Registered as a scoped service,
/// so each request gets its own instance and values can never leak between requests.
/// </summary>
internal sealed class TreblleMetadataCollector : ITreblleMetadata
{
    /// <summary>
    /// Upper bound on distinct keys set during a request, mirroring
    /// TreblleQueryCollector.MaxQueries. Once reached, new keys are dropped but existing ones
    /// can still be updated. The cap exists to bound growth driven by request data; keys
    /// configured once in TreblleOptions.Metadata are developer-authored and merged on top of
    /// this budget rather than counting against it.
    /// </summary>
    internal const int MaxEntries = 64;

    private readonly object _gate = new();

    // Allocated on first use so a request that never touches metadata pays nothing beyond
    // this object itself.
    private Dictionary<string, object?>? _values;

    public void Meta(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_gate)
        {
            var values = _values ??= new Dictionary<string, object?>(StringComparer.Ordinal);

            if (values.Count >= MaxEntries && !values.ContainsKey(key))
            {
                return;
            }

            values[key] = value;
        }
    }

    public void Meta(IDictionary<string, object?> values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var pair in values)
        {
            Meta(pair.Key, pair.Value);
        }
    }

    /// <summary>
    /// Copies the collected entries into <paramref name="target"/>, normalizing each value.
    /// Called while the request scope is still alive so that nothing user-supplied is held
    /// live across the background send.
    /// </summary>
    internal void CopyTo(IDictionary<string, object?> target)
    {
        lock (_gate)
        {
            if (_values is null)
            {
                return;
            }

            foreach (var pair in _values)
            {
                target[pair.Key] = TreblleMetadataNormalizer.Normalize(pair.Value);
            }
        }
    }
}
