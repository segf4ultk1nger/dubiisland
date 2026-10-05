using System;

namespace ClassIsland.Helpers;

/// <summary>
/// 滑窗 Levenshtein 相似度（移植自 wodeblog）。
/// </summary>
internal static class Fuzzy
{
    public static int PartialRatio(string query, string target)
    {
        if (query.Length == 0)
        {
            return target.Length == 0 ? 100 : 0;
        }

        if (target.Length == 0)
        {
            return 0;
        }

        if (target.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        var a = query.ToLowerInvariant();
        var b = target.ToLowerInvariant();
        if (a.Length > b.Length)
        {
            (a, b) = (b, a);
        }

        var best = 0;
        var window = a.Length;
        var max = b.Length - window;
        for (var i = 0; i <= max; i++)
        {
            var score = Ratio(a, b.AsSpan(i, window));
            if (score > best)
            {
                best = score;
                if (best == 100)
                {
                    return 100;
                }
            }
        }

        return best;
    }

    private static int Ratio(string a, ReadOnlySpan<char> b)
    {
        var dist = Levenshtein(a, b);
        var len = Math.Max(a.Length, b.Length);
        return (int)Math.Round(100.0 * (len - dist) / len);
    }

    private static int Levenshtein(string a, ReadOnlySpan<char> b)
    {
        var n = a.Length;
        var m = b.Length;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        for (var j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return prev[m];
    }
}
