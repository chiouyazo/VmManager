using System.Windows;
using System.Windows.Input;

namespace VmManager.Views.Dialogs;

/// <summary>Simple modal dialog that prompts the user for a new VM name.</summary>
public partial class RenameDialog : Window
{
    /// <summary>The name entered by the user. Only valid when <see cref="DialogResult"/> is true.</summary>
    public string NewName => NameBox.Text.Trim();

    public RenameDialog(string currentName, string? title = null, string? okText = null)
    {
        InitializeComponent();
        if (title != null)
            Title = title;
        if (okText != null && FindName("OkButton") is System.Windows.Controls.Button btn)
            btn.Content = okText;
        NameBox.Text = currentName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            return;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OK_Click(sender, e);
    }
}
