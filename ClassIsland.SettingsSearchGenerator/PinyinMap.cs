using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ClassIsland.SettingsSearchGenerator;

/// <summary>
/// 生成器内置的汉字→拼音表（<c>pinyin-map.txt</c>，十六进制码位 + 无声调拼音）。
/// 只在编译期使用，不进应用包。
/// </summary>
internal static class PinyinMap
{
    private static readonly Dictionary<char, string> Map = Load();

    /// <summary>紧凑全拼音，如「注册 Url」→ <c>zhuceurl</c>。</summary>
    public static string ToPinyin(string? text) => string.Concat(Tokenize(text));

    /// <summary>按音节/单词分隔的全拼音，如「注册 Url」→ <c>zhu ce url</c>。</summary>
    public static string ToSpacedPinyin(string? text) => string.Join(" ", Tokenize(text));

    /// <summary>拼音首字母（含原样保留的字母/数字），如「注册 Url」→ <c>zcurl</c>。</summary>
    public static string ToInitials(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new StringBuilder(text!.Length);
        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
                continue;
            }

            if (c is >= 'A' and <= 'Z')
            {
                sb.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (Map.TryGetValue(c, out var p) && p.Length > 0)
            {
                sb.Append(p[0]);
            }
        }

        return sb.ToString();
    }

    private static List<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        var ascii = new StringBuilder();
        foreach (var c in text!)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                ascii.Append(c);
                continue;
            }

            if (c is >= 'A' and <= 'Z')
            {
                ascii.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (ascii.Length > 0)
            {
                tokens.Add(ascii.ToString());
                ascii.Clear();
            }

            if (Map.TryGetValue(c, out var p) && p.Length > 0)
            {
                tokens.Add(p);
            }
        }

        if (ascii.Length > 0)
        {
            tokens.Add(ascii.ToString());
        }

        return tokens;
    }

    private static Dictionary<char, string> Load()
    {
        var map = new Dictionary<char, string>();
        var stream = typeof(PinyinMap).Assembly.GetManifestResourceStream("pinyin-map.txt");
        if (stream == null)
        {
            return map;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var space = line.IndexOf(' ');
            if (space <= 0)
            {
                continue;
            }

            if (int.TryParse(line.Substring(0, space), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var code) &&
                code is > 0 and <= 0xFFFF)
            {
                map[(char)code] = line.Substring(space + 1).Trim();
            }
        }

        return map;
    }
}
