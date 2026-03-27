using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VmManager.Converters;

public class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Error = new SolidColorBrush(
        new Color(255, 0xC4, 0x2B, 0x1C)
    );
    private static readonly SolidColorBrush Success = new SolidColorBrush(
        new Color(255, 0x10, 0x7C, 0x10)
    );

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Error : Success;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
