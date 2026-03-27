using System.Globalization;
using Avalonia.Data.Converters;

namespace VmManager.Converters;

public class BackendDisplayNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "HyperV" => "Hyper-V",
            "HyperV_External" => "Hyper-V (External)",
            "KVM" => "KVM",
            "KVM_External" => "KVM (External)",
            _ => value?.ToString() ?? "",
        };

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
