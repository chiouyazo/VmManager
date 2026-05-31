using System.Globalization;
using Avalonia.Data.Converters;

namespace VmManager.Converters;

public class StringEqualConverter : IMultiValueConverter
{
    public object Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        if (values.Count < 2)
            return false;

        string first = values[0]?.ToString() ?? "";
        string second = values[1]?.ToString() ?? "";
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }
}
