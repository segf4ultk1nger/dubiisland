using System;
using System.Globalization;
using System.Windows.Data;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Converters;

/// <summary>
/// 把设置页面的 Id 映射到 RemixIcon 图标。
/// </summary>
public class RemixIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "general" => PackIconRemixIconKind.Settings4Fill,
            "components" => PackIconRemixIconKind.AlignItemLeftFill,
            "appearance" => PackIconRemixIconKind.PaletteFill,
            "notification" => PackIconRemixIconKind.Notification3Fill,
            "window" => PackIconRemixIconKind.WindowFill,
            "weather" => PackIconRemixIconKind.CloudFill,
            "update" => PackIconRemixIconKind.DownloadCloudFill,
            "automation" => PackIconRemixIconKind.CodeBoxFill,
            "storage" => PackIconRemixIconKind.Database2Fill,
            "privacy" => PackIconRemixIconKind.ShieldCheckFill,
            "classisland.plugins" => PackIconRemixIconKind.Puzzle2Fill,
            "about" => PackIconRemixIconKind.Information2Fill,
            _ => PackIconRemixIconKind.Settings4Fill
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
