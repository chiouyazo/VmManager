using System.Windows;
using VmManager.ViewModels;
using VmManager.Views.Dialogs;

namespace VmManager.Views.Pages;

/// <summary>
/// Code-behind for the Snapshots page. Loads available VMs on first load.
/// </summary>
public partial class SnapshotsPage : System.Windows.Controls.Page
{
    private readonly SnapshotsViewModel _viewModel;

    /// <summary>Initialises the page and sets the data context.</summary>
    public SnapshotsPage(SnapshotsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnFirstLoaded;

        _viewModel.RequestVmName = defaultName =>
        {
            var dlg = new RenameDialog(defaultName, "Clone VM", "Clone");
            dlg.Owner = Window.GetWindow(this);
            return Task.FromResult(dlg.ShowDialog() == true ? dlg.NewName : (string?)null);
        };
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _ = _viewModel.LoadVmsAsync();
    }

    private async void CopyStatus_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.StatusMessage))
            return;
        try
        {
            System.Windows.Clipboard.SetText(_viewModel.StatusMessage);
            if (sender is System.Windows.Controls.Button btn)
            {
                btn.Content = "✓";
                await Task.Delay(1200);
                btn.Content = "📋";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Copy failed");
        }
    }
}
