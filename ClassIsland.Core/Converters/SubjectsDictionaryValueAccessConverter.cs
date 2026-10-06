using System.Globalization;
using System.Windows.Data;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Core.Converters;

public class SubjectsDictionaryValueAccessConverter : IValueConverter
{
    public ObservableDictionary<Guid, Subject> SourceDictionary
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
        try
        {
            return SourceDictionary[k].Name;
        }
        catch
        {
            return k.ToString();
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}