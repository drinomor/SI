using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SImulator.Converters;

/// <summary>
/// Converts a size kept in the settings as text ("3*", "260", "Auto") to a grid row / column size and back, so a
/// splitter's result is remembered.
/// </summary>
[ValueConversion(typeof(string), typeof(GridLength))]
public sealed class GridLengthStringConverter : IValueConverter
{
    private static readonly GridLengthConverter Converter = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            return value is string text && Converter.ConvertFromInvariantString(text) is GridLength length
                ? length
                : new GridLength(1, GridUnitType.Star);
        }
        catch (FormatException)
        {
            return new GridLength(1, GridUnitType.Star); // a damaged setting: an even share
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is GridLength length ? Converter.ConvertToInvariantString(length) ?? "*" : "*";
}
