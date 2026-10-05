using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Helpers;

/// <summary>
/// 天气代码到 RemixIcon（Fill）与 hero 渐变背景的映射。
/// </summary>
internal static class WeatherIconHelper
{
    public static PackIconRemixIconKind GetIcon(string? code, bool isNight = false)
    {
        return code switch
        {
            "0" => isNight ? PackIconRemixIconKind.MoonClearFill : PackIconRemixIconKind.SunFill,
            "1" => isNight ? PackIconRemixIconKind.MoonCloudyFill : PackIconRemixIconKind.SunCloudyFill,
            "2" => PackIconRemixIconKind.CloudyFill,
            "3" => PackIconRemixIconKind.ShowersFill,
            "4" => PackIconRemixIconKind.ThunderstormsFill,
            "5" => PackIconRemixIconKind.HailFill,
            "6" or "13" or "15" or "16" or "17" or "26" or "27" or "28" or "34" or "302" => PackIconRemixIconKind.SnowyFill,
            "7" or "19" => PackIconRemixIconKind.DrizzleFill,
            "8" or "21" or "301" => PackIconRemixIconKind.RainyFill,
            "9" or "10" or "11" or "12" or "22" or "23" or "24" or "25" => PackIconRemixIconKind.HeavyShowersFill,
            "14" => PackIconRemixIconKind.SnowflakeFill,
            "18" => isNight ? PackIconRemixIconKind.MoonFoggyFill : PackIconRemixIconKind.FoggyFill,
            "20" or "31" => PackIconRemixIconKind.Haze2Fill,
            "29" or "30" or "53" => PackIconRemixIconKind.HazeFill,
            "32" or "33" => PackIconRemixIconKind.TornadoFill,
            "35" => PackIconRemixIconKind.MistFill,
            _ => PackIconRemixIconKind.CloudyFill,
        };
    }

    /// <summary>
    /// 紫外线指数文字，如 "3 中等"。无数据时返回空串。
    /// </summary>
    public static string GetUvText(string? uvIndex)
    {
        if (string.IsNullOrWhiteSpace(uvIndex) ||
            !double.TryParse(uvIndex, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return "";
        }

        var level = value switch
        {
            < 3 => "弱",
            < 6 => "中等",
            < 8 => "强",
            < 11 => "很强",
            _ => "极强",
        };
        return $"{uvIndex} {level}";
    }

    public static Brush GetHeroBackground(string? code, bool isNight)
    {
        var (from, to) = GetGradientColors(code, isNight);
        return new LinearGradientBrush(from, to, 90.0);
    }

    private static (Color From, Color To) GetGradientColors(string? code, bool isNight)
    {
        return code switch
        {
            "0" => isNight
                ? (Color.FromRgb(0x0E, 0x1B, 0x33), Color.FromRgb(0x24, 0x36, 0x5B))
                : (Color.FromRgb(0x2E, 0x7D, 0xD1), Color.FromRgb(0x79, 0xB8, 0xE8)),
            "1" => (Color.FromRgb(0x5B, 0x87, 0xB5), Color.FromRgb(0x9D, 0xBB, 0xD8)),
            "2" => (Color.FromRgb(0x5E, 0x6B, 0x78), Color.FromRgb(0x8C, 0x98, 0xA5)),
            "3" or "7" or "8" or "9" or "10" or "11" or "12" or "19" or "21" or "22" or "23" or "24" or "25" or "301"
                => (Color.FromRgb(0x3A, 0x4A, 0x5E), Color.FromRgb(0x5E, 0x71, 0x88)),
            "4" or "5" => (Color.FromRgb(0x24, 0x1F, 0x3A), Color.FromRgb(0x4A, 0x3A, 0x63)),
            "6" or "13" or "14" or "15" or "16" or "17" or "26" or "27" or "28" or "34" or "302"
                => (Color.FromRgb(0x7E, 0x9A, 0xB8), Color.FromRgb(0xC9, 0xDB, 0xEB)),
            "18" or "35" => (Color.FromRgb(0x70, 0x7B, 0x85), Color.FromRgb(0xA6, 0xB0, 0xBA)),
            "20" or "29" or "30" or "31" or "53" => (Color.FromRgb(0x7A, 0x6A, 0x4E), Color.FromRgb(0xB9, 0xA4, 0x7E)),
            "32" or "33" => (Color.FromRgb(0x4C, 0x5A, 0x66), Color.FromRgb(0x7E, 0x8C, 0x99)),
            _ => (Color.FromRgb(0x55, 0x60, 0x6B), Color.FromRgb(0x8A, 0x97, 0xA3)),
        };
    }
}
