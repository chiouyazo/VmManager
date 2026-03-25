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

        // Wire up snapshot name prompt
        _viewModel.RequestVmName = defaultName =>
        {
            var dlg = new RenameDialog(defaultName, "Snapshot Name", "Create");
            dlg.Owner = Window.GetWindow(this);
            return Task.FromResult(dlg.ShowDialog() == true ? dlg.NewName : (string?)null);
        };

        // Wire up snapshot restore picker
        _viewModel.PickSnapshotForRestore = async vm =>
        {
            var snapshots = await viewModel._hyperVService.GetSnapshotsAsync(vm.Name);

            var items = new List<string>();
            var idMap = new Dictionary<int, string>();

            for (var i = 0; i < snapshots.Count; i++)
            {
                items.Add($"{snapshots[i].Name}  ({snapshots[i].CreationTime:yyyy-MM-dd HH:mm})");
                idMap[i] = snapshots[i].Id;
            }

            items.Add("Reset to base image");
            idMap[items.Count - 1] = "__base__";

            var picker = new SnapshotPickerDialog(items) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true || picker.SelectedIndex < 0)
                return null;

            return idMap[picker.SelectedIndex];
        };

        // Wire up navigation to snapshots page
        _viewModel.NavigateToSnapshots = vmName =>
        {
            var mainWindow = (Views.MainWindow)Window.GetWindow(this)!;
            mainWindow.PendingSnapshotVmName = vmName;
            mainWindow.NavigateToPage("Snapshots");
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
