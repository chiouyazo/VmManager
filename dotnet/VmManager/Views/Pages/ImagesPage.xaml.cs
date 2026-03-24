using System.Windows;
using VmManager.ViewModels;
using VmManager.Views.Dialogs;

namespace VmManager.Views.Pages;

public partial class ImagesPage : System.Windows.Controls.Page
{
    private readonly ImagesViewModel _viewModel;

    public ImagesPage(ImagesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnFirstLoaded;

        // Ask for VM name before creating
        _viewModel.RequestVmName = defaultName =>
        {
            var dialog = new RenameDialog(defaultName, "Name your VM", "Create")
            {
                Owner = Window.GetWindow(this),
            };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.NewName : null);
        };

        // Navigate to My VMs after creation
        _viewModel.NavigateTo = tag =>
        {
            if (Window.GetWindow(this) is Views.MainWindow mw)
                mw.NavigateToPage(tag);
        };
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _ = _viewModel.LoadCatalogAsync();
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
