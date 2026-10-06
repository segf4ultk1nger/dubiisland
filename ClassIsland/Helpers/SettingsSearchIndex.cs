using System;
using System.Collections.Generic;
using System.Linq;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Services.Registry;

namespace ClassIsland.Helpers;

/// <summary>
/// 设置搜索索引。条目（标题/描述/标签/拼音）在编译期由 <c>ClassIsland.SettingsSearchGenerator</c>
/// 从设置页 XAML 烘焙进 <see cref="SettingsSearchGenerated"/>，这里只做过滤与打分。
/// </summary>
internal static class SettingsSearchIndex
{
    private const int MaxHits = 60;
    private const int FuzzyMin = 70;

    private static List<SettingsSearchEntry>? _entries;

    public static bool IsBuilt => _entries != null;

    public static void Reset() => _entries = null;

    /// <summary>按当前可见的设置页过滤生成好的索引。</summary>
    public static void Build(Func<SettingsPageInfo, bool> filter)
    {
        var allowed = new HashSet<string>();
        foreach (var page in SettingsWindowRegistryService.Registered)
        {
            if (filter(page))
            {
                allowed.Add(page.Id);
            }
        }

        _entries = SettingsSearchGenerated.Entries.Where(e => allowed.Contains(e.PageId)).ToList();
    }

    public static List<SettingsSearchEntry> Search(string? query)
    {
        var entries = _entries;
        if (entries == null)
        {
            return new List<SettingsSearchEntry>();
        }

        query = (query ?? "").Trim();
        if (query.Length == 0)
        {
            return new List<SettingsSearchEntry>();
        }

        var ql = query.ToLowerInvariant();
        var scored = new List<(int Score, SettingsSearchEntry Entry)>();
        foreach (var entry in entries)
        {
            // 当前被隐藏的设置（可见性条件不满足）不参与搜索。
            if (entry.Visible is { } visible && !visible())
            {
                continue;
            }

            var score = Score(entry, query, ql);
            if (score > 0)
            {
                scored.Add((score, entry));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Header.Length)
            .Take(MaxHits)
            .Select(x => x.Entry)
            .ToList();
    }

    private static int Score(SettingsSearchEntry e, string q, string ql)
    {
        // 中文 / 字面
        if (e.Header.Equals(q, StringComparison.OrdinalIgnoreCase)) return 1100;
        if (e.Header.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 1050;
        if (e.Header.Contains(q, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (e.Tags.Contains(q, StringComparison.OrdinalIgnoreCase)) return 900;
        if (e.PageName.Contains(q, StringComparison.OrdinalIgnoreCase)) return 500;
        if (e.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) return 400;

        // 拼音 / 首字母（紧凑，无空格查询）
        if (ql.Length > 0 && IsAsciiLetters(ql))
        {
            if (e.Initials == ql) return 1000;
            if (e.Initials.StartsWith(ql, StringComparison.Ordinal)) return 950;
            if (e.Pinyin.StartsWith(ql, StringComparison.Ordinal)) return 900;
            if (e.Pinyin.Contains(ql, StringComparison.Ordinal)) return 700;
            if (e.Initials.Contains(ql, StringComparison.Ordinal)) return 650;
        }

        // 拼音（查询可带音节空格，如「zhu ce」）
        if (ql.Length > 0 && IsAsciiLettersOrSpaces(ql) &&
            e.PinyinSpaced.Contains(ql, StringComparison.Ordinal))
        {
            return 690;
        }

        // 模糊：紧凑拼音容忍漏字（zuce≈zhuce），带空格拼音容忍多字/音节错位（zhuice≈zhu ce），取最大。
        if (q.Length >= 2)
        {
            var fuzzy = Math.Max(
                Fuzzy.PartialRatio(q, e.Header),
                Math.Max(
                    Math.Max(Fuzzy.PartialRatio(ql, e.Pinyin), Fuzzy.PartialRatio(ql, e.PinyinSpaced)),
                    Fuzzy.PartialRatio(ql, e.Initials)));
            if (fuzzy >= FuzzyMin)
            {
                return fuzzy;
            }
        }

        return 0;
    }

    private static bool IsAsciiLetters(string s)
    {
        foreach (var c in s)
        {
            if (c is < 'a' or > 'z')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLettersOrSpaces(string s)
    {
        foreach (var c in s)
        {
            if (c is < 'a' or > 'z' && c != ' ')
            {
                return false;
            }
        }

        return true;
    }
}
