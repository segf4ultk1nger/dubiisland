using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 从 <see cref="PackIconRemixIconKind"/> 的 Description（形如 "alert-line (System, EA21)"）中取出字形字符。
/// </summary>
public static class RemixIconGlyph
{
    private static readonly Dictionary<PackIconRemixIconKind, string> Cache = new();

    public static string Get(PackIconRemixIconKind kind)
    {
        if (Cache.TryGetValue(kind, out var cached))
            return cached;

        var description = typeof(PackIconRemixIconKind).GetField(kind.ToString())
            ?.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var match = description is null
            ? null
            : Regex.Match(description, @"([0-9A-Fa-f]{4,6})\)\s*$");
        var glyph = match is { Success: true }
            ? char.ConvertFromUtf32(Convert.ToInt32(match.Groups[1].Value, 16))
            : "";
        Cache[kind] = glyph;
        return glyph;
    }

    /// <summary>RemixIcon 图标字体名。</summary>
    public const string FontFamily = "MahApps.Metro.IconPacks.RemixIcon";
}
