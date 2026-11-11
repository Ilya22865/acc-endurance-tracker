using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EnduranceHub.Client.Converters;

public sealed class IntEqualsBoolConverter : IValueConverter
{
    public static readonly IntEqualsBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int actual || parameter is not string expected) return false;
        return actual == int.Parse(expected, CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}