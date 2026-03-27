using System.Globalization;
using Avalonia.Data.Converters;

namespace VmManager.Converters;

public class VmStateIsRunningConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == "Running";

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
