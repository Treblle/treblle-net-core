using System;
using System.Collections.Generic;

namespace Treblle.Net.Core.Masking;

/// <summary>
/// An immutable, pre-compiled set of field/masker rules.
/// </summary>
/// <remarks>
/// Built once per registration and never mutated. The previous design merged user overrides
/// into a process-wide static dictionary at service-resolution time, while the background
/// sender was enumerating that same dictionary — a concurrent write and read on
/// <see cref="Dictionary{TKey,TValue}"/> can corrupt the bucket chain and spin the reading
/// thread indefinitely.
/// <para>
/// Keys are prepared here rather than on every comparison. Matching uses
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, so walking a payload allocates nothing
/// per property — the previous <c>ToLower()</c> on both sides allocated two strings for every
/// (property × rule) pair. Ordinal case folding is also culture-independent, so a host running
/// a Turkish culture no longer fails to match keys containing "I".
/// </para>
/// </remarks>
internal sealed class MaskingRules
{
    private readonly Rule[] _rules;

    internal MaskingRules(IEnumerable<KeyValuePair<string, string>> map)
    {
        var rules = new List<Rule>();

        foreach (var entry in map)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            rules.Add(new Rule(entry.Key, entry.Value));
        }

        _rules = rules.ToArray();
    }

    internal bool IsEmpty => _rules.Length == 0;

    /// <summary>
    /// Returns the masker name for the first rule matching <paramref name="path"/>, or null
    /// when no rule applies. <paramref name="path"/> is the dotted path of the property within
    /// the payload, for example <c>data.request.body.password</c>.
    /// </summary>
    internal string? FindMaskerName(string path)
    {
        foreach (var rule in _rules)
        {
            if (rule.Matches(path))
            {
                return rule.MaskerName;
            }
        }

        return null;
    }

    private readonly struct Rule
    {
        private readonly string _key;

        // Set only for a path key ending in a wildcard, such as "account.*".
        private readonly string? _prefix;

        // Set only for a bare key, so that "password" matches at any depth via ".password".
        private readonly string? _dotted;

        internal Rule(string key, string maskerName)
        {
            MaskerName = maskerName;
            _key = key;

            if (key.IndexOf('.') >= 0)
            {
                _prefix = key.EndsWith("*", StringComparison.Ordinal)
                    ? key.Substring(0, key.Length - 1)
                    : null;
                _dotted = null;
            }
            else
            {
                _prefix = null;
                _dotted = "." + key;
            }
        }

        internal string MaskerName { get; }

        internal bool Matches(string path)
        {
            if (_dotted is not null)
            {
                return path.Equals(_key, StringComparison.OrdinalIgnoreCase)
                    || path.Contains(_dotted, StringComparison.OrdinalIgnoreCase);
            }

            return path.Contains(_key, StringComparison.OrdinalIgnoreCase)
                || (_prefix is not null && path.Contains(_prefix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
