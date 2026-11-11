using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EnduranceHub.Client.Converters;

public sealed class BoolToBrushConverter : IValueConverter
{
    public static BoolToBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "#FFFFFF;#707070").Split(';');
        var color = value is true ? parts[0].Trim() : parts[1].Trim();
        return new SolidColorBrush(Color.Parse(color));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}