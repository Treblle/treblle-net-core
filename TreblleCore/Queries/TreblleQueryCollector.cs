using System;
using System.Collections.Generic;
using System.Threading;

namespace Treblle.Net.Core;

internal static class TreblleQueryCollector
{
    private static readonly AsyncLocal<List<QueryEntry>?> _queries = new();
    private const int MaxQueries = 100;

    internal static void Initialize()
    {
        _queries.Value = new List<QueryEntry>(16);
    }

    internal static void Add(string sql, double timeMs)
    {
        var list = _queries.Value;
        if (list is not null && list.Count < MaxQueries)
        {
            list.Add(new QueryEntry { Sql = sql, Time = Math.Round(timeMs, 2) });
        }
    }

    internal static IReadOnlyList<QueryEntry> GetQueries()
        => (IReadOnlyList<QueryEntry>?)_queries.Value ?? Array.Empty<QueryEntry>();
}
