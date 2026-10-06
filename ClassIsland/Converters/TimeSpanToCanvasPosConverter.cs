using System;
using System.Globalization;
using System.Windows.Data;

namespace ClassIsland.Converters;

public class TimeSpanToCanvasPosConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var v = values[0] switch
        {
            TimeSpan time => time,
            DateTime dateTime => dateTime.TimeOfDay,
            _ => TimeSpan.Zero
        };
        var s = values[1] as double? ?? 1.0;
        return v.Ticks / 1000000000.0 * s;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return Array.Empty<object>();
    }
}
