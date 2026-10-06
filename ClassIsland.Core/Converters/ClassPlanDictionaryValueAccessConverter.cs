using System.Globalization;
using System.Windows.Data;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Core.Converters;

public class ClassPlanDictionaryValueAccessConverter : IValueConverter
{
    public ObservableDictionary<Guid, TimeLayout> SourceDictionary
    {
        get;
        set;
    } = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Guid k)
        {
            return null;
        }
        return k == Guid.Empty ? null : new KeyValuePair<Guid, TimeLayout>(k, SourceDictionary[k]);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var k = (KeyValuePair<Guid, TimeLayout>?)value ?? new KeyValuePair<Guid, TimeLayout>();
        return k.Key;
    }
}