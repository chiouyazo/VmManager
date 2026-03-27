using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VmManager.Views.Dialogs;

public partial class RenameDialog : Window
{
    public string NewName => NameBox.Text?.Trim() ?? "";

    public RenameDialog(string currentName, string? title = null, string? okText = null)
    {
        InitializeComponent();
        if (title != null)
            Title = title;
        if (okText != null && this.FindControl<Button>("OkButton") is Button btn)
            btn.Content = okText;
        NameBox.Text = currentName;
        NameBox.AttachedToVisualTree += (_, _) =>
        {
            NameBox.SelectAll();
            NameBox.Focus();
        };
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            return;

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void NameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OK_Click(sender, e);
    }
}
