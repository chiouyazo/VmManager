using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VmManager.Views.Dialogs;

public partial class ChangePasswordDialog : Window
{
    private bool _isSubmitting;
    private bool _passwordChanged;

    public ChangePasswordDialog()
    {
        InitializeComponent();

        PasswordBox.AttachedToVisualTree += (_, _) => PasswordBox.Focus();
        Closing += (_, e) =>
        {
            if (!_passwordChanged)
                e.Cancel = true;
        };
    }

    private async void Change_Click(object? sender, RoutedEventArgs e)
    {
        await SubmitAsync();
    }

    private async void PasswordBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        if (_isSubmitting)
            return;

        string password = PasswordBox.Text?.Trim() ?? "";
        string confirm = ConfirmBox.Text?.Trim() ?? "";

        if (password.Length < 6)
        {
            ShowError("Password must be at least 6 characters.");
            return;
        }

        if (password != confirm)
        {
            ShowError("Passwords do not match.");
            return;
        }

        _isSubmitting = true;
        ChangeButton.IsEnabled = false;
        ErrorText.IsVisible = false;

        try
        {
            await App.AgentClient!.ChangeOwnPasswordAsync(password);
            _passwordChanged = true;
            Close(true);
        }
        catch (Exception ex)
        {
            ShowError("Failed to change password: " + ex.Message);
        }
        finally
        {
            _isSubmitting = false;
            ChangeButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
