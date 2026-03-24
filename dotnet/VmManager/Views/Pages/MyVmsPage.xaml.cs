using System.Windows;
using System.Windows.Controls;
using VmManager.Models;
using VmManager.ViewModels;
using VmManager.Views.Dialogs;

namespace VmManager.Views.Pages;

public partial class MyVmsPage : System.Windows.Controls.Page
{
    private readonly MyVmsViewModel _viewModel;

    public MyVmsPage(MyVmsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnFirstLoaded;

        // Wire up confirmation dialogs
        _viewModel.ConfirmAction = (title, message) =>
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            return Task.FromResult(result == MessageBoxResult.Yes);
        };
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _ = _viewModel.RefreshAsync();
    }

    private async void CopyStatus_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.StatusMessage))
            return;
        try
        {
            System.Windows.Clipboard.SetText(_viewModel.StatusMessage);
            if (sender is Button btn)
            {
                btn.Content = "✓";
                await Task.Delay(1200);
                btn.Content = "📋";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy failed");
        }
    }

    private async void CopyCredential_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string text)
            return;
        try
        {
            System.Windows.Clipboard.SetText(text);
            var original = btn.Content;
            btn.Content = "✓";
            await Task.Delay(1200);
            btn.Content = original;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy failed");
        }
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not VmInstance vm)
            return;

        var dialog = new RenameDialog(vm.Name) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
            return;

        vm.PendingRename = dialog.NewName;
        await _viewModel.RenameVmAsync(vm);
    }

    private void NoteBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is VmInstance vm)
        {
            vm.Notes = tb.Text;
            _viewModel.SaveNoteForVmCommand.Execute(vm);
        }
    }
}
