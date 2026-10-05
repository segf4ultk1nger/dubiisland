using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Helpers;

/// <summary>
/// 气象预警等级颜色与类型图标的映射。
/// </summary>
internal static class WeatherAlertHelper
{
    public static Color GetLevelColor(string? level)
    {
        if (string.IsNullOrEmpty(level))
        {
            return Color.FromRgb(0x9C, 0xA3, 0xAF);
        }

        if (level.Contains("红"))
        {
            return Color.FromRgb(0xDC, 0x26, 0x26);
        }

        if (level.Contains("橙"))
        {
            return Color.FromRgb(0xF9, 0x73, 0x16);
        }

        if (level.Contains("黄"))
        {
            return Color.FromRgb(0xEA, 0xB3, 0x08);
        }

        if (level.Contains("蓝"))
        {
            return Color.FromRgb(0x3B, 0x82, 0xF6);
        }

        return Color.FromRgb(0x9C, 0xA3, 0xAF);
    }

    public static PackIconRemixIconKind GetTypeIcon(string? type)
    {
        type ??= "";
        if (type.Contains("雷"))
        {
            return PackIconRemixIconKind.ThunderstormsFill;
        }

        if (type.Contains("台风"))
        {
            return PackIconRemixIconKind.TyphoonFill;
        }

        if (type.Contains("龙卷"))
        {
            return PackIconRemixIconKind.TornadoFill;
        }

        if (type.Contains("洪水") || type.Contains("内涝"))
        {
            return PackIconRemixIconKind.FloodFill;
        }

        if (type.Contains("冰雹"))
        {
            return PackIconRemixIconKind.HailFill;
        }

        if (type.Contains("雪") || type.Contains("寒潮") || type.Contains("低温") ||
            type.Contains("霜") || type.Contains("冰冻") || type.Contains("结冰"))
        {
            return PackIconRemixIconKind.SnowyFill;
        }

        if (type.Contains("雨"))
        {
            return PackIconRemixIconKind.RainyFill;
        }

        if (type.Contains("风"))
        {
            return PackIconRemixIconKind.WindyFill;
        }

        if (type.Contains("雾"))
        {
            return PackIconRemixIconKind.FoggyFill;
        }

        if (type.Contains("霾"))
        {
            return PackIconRemixIconKind.HazeFill;
        }

        if (type.Contains("沙") || type.Contains("尘"))
        {
            return PackIconRemixIconKind.Haze2Fill;
        }

        if (type.Contains("高温") || type.Contains("热") || type.Contains("干旱"))
        {
            return PackIconRemixIconKind.SunFill;
        }

        if (type.Contains("火"))
        {
            return PackIconRemixIconKind.FireFill;
        }

        if (type.Contains("地震"))
        {
            return PackIconRemixIconKind.EarthquakeFill;
        }

        return PackIconRemixIconKind.AlertFill;
    }
}
