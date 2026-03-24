using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace VmManager.Converters;

/// <summary>Converts a <see cref="bool"/> to <see cref="Visibility.Visible"/> (true) or <see cref="Visibility.Collapsed"/> (false).</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => value is Visibility.Visible;
}

/// <summary>Converts a <see cref="bool"/> to <see cref="Visibility.Collapsed"/> (true) or <see cref="Visibility.Visible"/> (false).</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => value is Visibility.Collapsed;
}

/// <summary>Inverts a boolean value.</summary>
public class InverseBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => value is bool b && !b;
}

/// <summary>
/// Converts a boolean error flag to a status background <see cref="SolidColorBrush"/>.
/// <c>true</c> → red (error), <c>false</c> → green (success).
/// </summary>
public class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Error = new(Color.FromRgb(0xC4, 0x2B, 0x1C));
    private static readonly SolidColorBrush Success = new(Color.FromRgb(0x10, 0x7C, 0x10));

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Error : Success;

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

/// <summary>
/// Maps a Hyper-V VM state string to a coloured <see cref="SolidColorBrush"/> for the state badge.
/// </summary>
public class VmStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = new(Color.FromRgb(0x10, 0x7C, 0x10)); // green
    private static readonly SolidColorBrush Off = new(Color.FromRgb(0x60, 0x60, 0x60)); // grey
    private static readonly SolidColorBrush Starting = new(Color.FromRgb(0xCA, 0x50, 0x10)); // orange
    private static readonly SolidColorBrush Other = new(Color.FromRgb(0x00, 0x57, 0x9B)); // blue

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Running" => Running,
            "Off" => Off,
            "Starting" or "Resuming" => Starting,
            _ => Other,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

/// <summary>Returns <see cref="Visibility.Visible"/> when the string is non-empty, otherwise <see cref="Visibility.Collapsed"/>.</summary>
public class StringToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

/// <summary>Returns <see cref="Visibility.Visible"/> when an integer count is zero, otherwise <see cref="Visibility.Collapsed"/>.</summary>
public class ZeroCountToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i)
            return i == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

/// <summary>Converts the internal backend key ("HyperV") to a display-friendly name ("Hyper-V").</summary>
public class BackendDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "HyperV" => "Hyper-V",
            _ => value?.ToString() ?? "",
        };

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

/// <summary>Returns false when the string equals "Docker", true otherwise. Used to collapse Docker group by default.</summary>
public class BackendToExpandedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() != "Docker";

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
