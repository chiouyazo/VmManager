using System.Globalization;
using Avalonia.Data.Converters;

namespace VmManager.Converters;

public class BackendToExpandedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() is "HyperV" or "KVM" or "Proxmox";

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
