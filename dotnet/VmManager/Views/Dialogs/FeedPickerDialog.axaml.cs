using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VmManager.Views.Dialogs;

public partial class FeedPickerDialog : Window
{
    public int SelectedIndex => FeedCombo.SelectedIndex;

    public FeedPickerDialog(
        List<string> items,
        string? title = null,
        string? message = null,
        string? okText = null
    )
    {
        InitializeComponent();
        FeedCombo.ItemsSource = items;
        if (items.Count > 0)
            FeedCombo.SelectedIndex = 0;
        if (title != null)
            Title = title;
        if (message != null)
            MessageText.Text = message;
        if (okText != null)
            OkButton.Content = okText;
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (FeedCombo.SelectedIndex < 0)
            return;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
