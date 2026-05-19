using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VmManager.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(
        string title,
        string message,
        bool isDangerous = true,
        string confirmText = "Confirm",
        string? cancelText = "Cancel"
    )
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        string themeKey =
            (cancelText == null) ? "PrimaryButtonStyle"
            : isDangerous ? "DangerButtonStyle"
            : "PrimaryButtonStyle";

        AttachedToVisualTree += (_, _) =>
        {
            if (
                this.TryFindResource(themeKey, this.ActualThemeVariant, out var theme)
                && theme is Avalonia.Styling.ControlTheme ctrl
            )
                ConfirmButton.Theme = ctrl;
        };

        if (cancelText == null)
        {
            CancelButton.IsVisible = false;
        }
        else
        {
            CancelButton.Content = cancelText;
            CancelButton.AttachedToVisualTree += (_, _) => CancelButton.Focus();
        }
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
