using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using ClassIsland.Core.Models.Weather;

namespace ClassIsland.Helpers;

/// <summary>
/// 本地中国城市索引：内置 2566 个地区 + 预生成拼音，离线做精确/拼音/模糊搜索。
/// </summary>
internal static class CitySearchIndex
{
    private sealed class Entry
    {
        public string Name = "";
        public string Province = "";
        public string CityId = "";
        public string Py = "";
        public string Ini = "";
        public string Primary = "";
        public string Region = "";
    }

    private const int MaxHits = 80;
    private const int FuzzyMin = 70;

    private static readonly string[] HotCityIds =
    {
        "weathercn:101010100", "weathercn:101020100", "weathercn:101280101", "weathercn:101280601",
        "weathercn:101280701", "weathercn:101280800", "weathercn:101190101", "weathercn:101190401",
        "weathercn:101230201", "weathercn:101300101", "weathercn:101290101", "weathercn:101270101",
        "weathercn:101250101", "weathercn:101230101", "weathercn:101210101", "weathercn:101200101",
        "weathercn:101120201", "weathercn:101110101", "weathercn:101100101", "weathercn:101090101",
        "weathercn:101070101", "weathercn:101040100", "weathercn:101030100"
    };

    private static readonly Lazy<List<Entry>> Entries = new(Load);

    public static List<City> Search(string? query)
    {
        query = (query ?? "").Trim();

        if (query.Length == 0)
        {
            return Entries.Value
                .Where(e => HotCityIds.Contains(e.CityId))
                .OrderBy(e => Array.IndexOf(HotCityIds, e.CityId))
                .Select(ToCity)
                .ToList();
        }

        var ql = query.ToLowerInvariant();
        var scored = new List<(int Score, Entry Entry)>();
        foreach (var e in Entries.Value)
        {
            var score = Score(e, query, ql);
            if (score > 0)
            {
                scored.Add((score, e));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Name.Length)
            .Take(MaxHits)
            .Select(x => ToCity(x.Entry))
            .ToList();
    }

    private static int Score(Entry e, string q, string ql)
    {
        // 中文
        if (e.Primary.Equals(q, StringComparison.OrdinalIgnoreCase)) return 1100;
        if (e.Primary.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 1050;
        if (e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (e.Province.Length > 0 && e.Province.Contains(q, StringComparison.OrdinalIgnoreCase)) return 400;

        // 拼音 / 首字母
        if (ql.Length > 0 && IsAsciiLetters(ql))
        {
            if (e.Ini == ql) return 1000;
            if (e.Ini.StartsWith(ql, StringComparison.Ordinal)) return 950;
            if (e.Py.StartsWith(ql, StringComparison.Ordinal)) return 900;
            if (e.Py.Contains(ql, StringComparison.Ordinal)) return 700;
            if (e.Ini.Contains(ql, StringComparison.Ordinal)) return 650;
        }

        // 模糊
        if (q.Length >= 2)
        {
            var fuzzy = Math.Max(
                Fuzzy.PartialRatio(q, e.Primary),
                Math.Max(Fuzzy.PartialRatio(ql, e.Py), Fuzzy.PartialRatio(q, e.Name)));
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

    private static City ToCity(Entry e) => new()
    {
        Name = e.Primary,
        Region = e.Region,
        CityId = e.CityId
    };

    private static List<Entry> Load()
    {
        var list = new List<Entry>();
        var uri = new Uri("/Assets/Weather/cities.tsv", UriKind.Relative);
        var stream = Application.GetResourceStream(uri)?.Stream;
        if (stream == null)
        {
            return list;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 5)
            {
                continue;
            }

            var name = parts[0];
            var province = parts[1];
            var cityNum = parts[2];

            string primary;
            string region;
            var dot = name.IndexOf('.');
            if (dot >= 0)
            {
                primary = name[(dot + 1)..];
                region = name[..dot];
            }
            else
            {
                primary = name;
                region = province == primary ? "" : province;
            }

            list.Add(new Entry
            {
                Name = name,
                Province = province,
                CityId = "weathercn:" + cityNum,
                Py = parts[3],
                Ini = parts[4],
                Primary = primary,
                Region = region
            });
        }

        return list;
    }
}
