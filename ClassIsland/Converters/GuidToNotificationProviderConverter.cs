using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using ClassIsland.Core.Abstractions.Services;

namespace ClassIsland.Converters;

public class GuidToNotificationProviderConverter : IValueConverter
{
    private static INotificationHostService? _service;

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }
        var id = (string)value;
        _service ??= App.GetService<INotificationHostService>();
        return _service.NotificationProviders.FirstOrDefault(i => i.ProviderGuid.ToString() == id);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;
    }
}