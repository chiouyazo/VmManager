using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VmManager.Converters;

public class FilterActiveBrushConverter : IValueConverter
{
    public IBrush? ActiveBrush { get; set; }
    public IBrush? InactiveBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string currentFilter = value?.ToString() ?? "All";
        string targetFilter = parameter?.ToString() ?? "All";
        bool isActive = string.Equals(
            currentFilter,
            targetFilter,
            StringComparison.OrdinalIgnoreCase
        );
        return isActive ? ActiveBrush : InactiveBrush;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotSupportedException();
    }
}
