using System;
using System.Globalization;
using System.Windows.Data;

namespace ClassIsland.Core.Converters;

/// <summary>
/// 将 [比例(0~1), 总宽度] 转换为实际像素宽度，用于存储占用比例条。
/// </summary>
public class RatioToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double ratio || values[1] is not double total)
        {
            return 0d;
        }

        if (double.IsNaN(ratio) || double.IsNaN(total) || total <= 0)
        {
            return 0d;
        }

        return Math.Max(0d, ratio * total);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
