using System;
using System.Globalization;
using System.Windows.Data;

namespace ClassIsland.Converters;

/// <summary>
/// 对齐 Core 的 <c>StringToRadioButtonSelectionConverter</c>，但支持 Guid 键。
/// </summary>
public class GuidToStringRadioButtonSelectionConverter : IMultiValueConverter
{
    private object? _valueRaw;

    public object Convert(object[] value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value.Length < 2)
        {
            return false;
        }
        _valueRaw = value[1];
        return Equals(value[0], value[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return [_valueRaw!, _valueRaw!];
        }

        return null!;
    }
}
