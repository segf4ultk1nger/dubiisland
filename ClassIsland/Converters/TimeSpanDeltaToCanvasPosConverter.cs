using System;
using System.Globalization;
using System.Windows.Data;

namespace ClassIsland.Converters;

public class TimeSpanDeltaToCanvasPosConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var v1 = ToTimeSpan(values[0]);
        var v2 = ToTimeSpan(values[1]);
        var s = values[2] as double? ?? 1.0;
        return Math.Max((v2 - v1).Ticks / 1000000000.0 * s, 1.0);
    }

    private static TimeSpan ToTimeSpan(object? value) => value switch
    {
        TimeSpan time => time,
        DateTime dateTime => dateTime.TimeOfDay,
        _ => TimeSpan.Zero
    };

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return Array.Empty<object>();
    }
}
