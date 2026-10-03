using System;
using System.Globalization;
using System.Windows.Data;

namespace ClassIsland.Core.Converters;

/// <summary>
/// Converts a time-of-day <see cref="TimeSpan"/> to a <see cref="DateTime"/> on today's date.
/// </summary>
public class TimeSpanToDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            TimeSpan time => DateTime.Today + time,
            DateTime dateTime => dateTime,
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.TimeOfDay;
        }

        return Binding.DoNothing;
    }
}
