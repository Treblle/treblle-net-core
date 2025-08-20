using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Treblle.Net.Core;

/// <summary>
/// High-performance path matching utility with wildcard support and caching.
/// Supports patterns like "/admin/*", "/health", "/api/v*/users", etc.
/// </summary>
internal static class PathMatcher
{
    // Cache compiled regex patterns for performance (bounded to prevent memory leaks)
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    
    // Cache match results to avoid repeated evaluations for the same path
    // Use path-only as key to prevent pattern-based explosion
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _matchCache = new();

    /// <summary>
    /// Checks if a path should be excluded based on the provided exclusion patterns.
    /// Uses case-insensitive matching with performance optimizations.
    /// </summary>
    /// <param name="path">The request path to check</param>
    /// <param name="excludedPaths">Array of exclusion patterns (can contain wildcards)</param>
    /// <returns>True if the path should be excluded, false otherwise</returns>
    public static bool ShouldExcludePath(string path, string[]? excludedPaths)
    {
        if (excludedPaths == null || excludedPaths.Length == 0)
            return false;

        if (string.IsNullOrEmpty(path))
            return false;

        // Check cache first - use path as primary key to prevent memory explosion
        if (_matchCache.TryGetValue(path, out var pathCache))
        {
            // Create a stable hash of patterns for secondary key
            var patternHash = GetPatternHash(excludedPaths);
            if (pathCache.TryGetValue(patternHash, out bool cachedResult))
                return cachedResult;
        }

        bool shouldExclude = false;

        // Normalize path (ensure it starts with /)
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;

        foreach (var pattern in excludedPaths)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            var normalizedPattern = pattern.StartsWith('/') ? pattern : "/" + pattern;

            // Fast path: exact match (most common case)
            if (string.Equals(normalizedPath, normalizedPattern, StringComparison.OrdinalIgnoreCase))
            {
                shouldExclude = true;
                break;
            }

            // Check if pattern contains wildcards
            if (normalizedPattern.Contains('*') || normalizedPattern.Contains('?'))
            {
                if (MatchesWildcardPattern(normalizedPath, normalizedPattern))
                {
                    shouldExclude = true;
                    break;
                }
            }
        }

        // Cache the result with bounded memory usage
        if (_matchCache.Count < 1000) // Limit number of unique paths
        {
            var pathCacheEntry = _matchCache.GetOrAdd(path, _ => new ConcurrentDictionary<string, bool>());
            if (pathCacheEntry.Count < 10) // Limit patterns per path
            {
                var patternHash = GetPatternHash(excludedPaths);
                pathCacheEntry.TryAdd(patternHash, shouldExclude);
            }
        }

        return shouldExclude;
    }

    /// <summary>
    /// Matches a path against a wildcard pattern using compiled regex for performance.
    /// </summary>
    private static bool MatchesWildcardPattern(string path, string pattern)
    {
        // Get or create compiled regex pattern
        var regex = _regexCache.GetOrAdd(pattern, CreateRegexFromWildcard);
        return regex.IsMatch(path);
    }

    /// <summary>
    /// Converts a wildcard pattern to a compiled regex for optimal performance.
    /// </summary>
    private static Regex CreateRegexFromWildcard(string pattern)
    {
        try
        {
            // Validate pattern length to prevent ReDoS
            if (pattern.Length > 200)
                throw new ArgumentException("Pattern too long for security reasons");

            // Escape special regex characters except * and ?
            var escaped = Regex.Escape(pattern)
                .Replace("\\*", ".*")  // * matches any number of characters
                .Replace("\\?", ".");  // ? matches exactly one character

            // Add anchors to ensure full path matching
            var regexPattern = $"^{escaped}$";

            // Use timeout to prevent ReDoS attacks
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            // Return a regex that never matches if pattern is invalid
            return new Regex("(?!.*)", RegexOptions.Compiled);
        }
    }

    /// <summary>
    /// Creates a stable hash of pattern array for caching
    /// </summary>
    private static string GetPatternHash(string[] patterns)
    {
        // Simple hash to avoid string.Join allocation on every request
        var hash = 0;
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                hash = hash * 31 + pattern.GetHashCode(StringComparison.OrdinalIgnoreCase);
            }
        }
        return hash.ToString();
    }

    /// <summary>
    /// Clears internal caches. Useful for testing or if memory usage becomes a concern.
    /// </summary>
    public static void ClearCaches()
    {
        _regexCache.Clear();
        _matchCache.Clear();
    }
}