using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VmManager.ViewModels;
using Res = VmManager.Properties.Resources;

namespace VmManager.Views.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.RequestBrowseFolder = async _ =>
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return null;
            IReadOnlyList<IStorageFolder> folders =
                await topLevel.StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions { Title = Res.Settings_SelectVmFolder }
                );
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };

        _viewModel.OnAgentsSaved = () =>
        {
            if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
            {
                mainWindow.PopulateEnvironmentSelector();
                mainWindow.ReconnectCurrentAgent();
            }
        };

        _viewModel.OnSettingsSaved = () => { };
    }

    private async void CopyStatus_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.StatusMessage))
            return;
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
                await topLevel.Clipboard.SetTextAsync(_viewModel.StatusMessage);
            if (sender is Button btn)
            {
                object? original = btn.Content;
                btn.Content = "Done";
                await Task.Delay(1200);
                btn.Content = original;
            }
        }
        catch { }
    }
}
