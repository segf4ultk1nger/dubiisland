using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClassIsland.Core.Converters;

/// <summary>
/// 根据背景色的感知亮度返回黑色或白色前景，保证在任何背景色上都清晰可辨。
/// 亮度采用心理学公式：灰度 = 0.299R + 0.587G + 0.114B。
/// </summary>
public class ColorToLuminanceForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Color color)
        {
            return Brushes.Black;
        }

        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance > 0.5 ? Brushes.Black : Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
