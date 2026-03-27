using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VmManager.Converters;

public class VmStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = new SolidColorBrush(
        new Color(255, 0x10, 0x7C, 0x10)
    );
    private static readonly SolidColorBrush Off = new SolidColorBrush(
        new Color(255, 0x60, 0x60, 0x60)
    );
    private static readonly SolidColorBrush Starting = new SolidColorBrush(
        new Color(255, 0xCA, 0x50, 0x10)
    );
    private static readonly SolidColorBrush Other = new SolidColorBrush(
        new Color(255, 0x00, 0x57, 0x9B)
    );

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Running" => Running,
            "Off" => Off,
            "Starting" or "Resuming" => Starting,
            _ => Other,
        };
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
