using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using VmManager.Models;
using VmManager.Services;
using VmManager.ViewModels;
using VmManager.Views.Dialogs;
using Res = VmManager.Properties.Resources;

namespace VmManager.Views.Pages;

public partial class MyVmsPage : UserControl
{
    private readonly MyVmsViewModel _viewModel;

    public MyVmsPage(MyVmsViewModel viewModel, NotificationService notificationService)
    {
        _viewModel = viewModel;
        _viewModel.Notifications = notificationService;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.ConfirmAction = async (title, message) =>
        {
            ConfirmDialog dlg = new ConfirmDialog(title, message);
            bool? result = await dlg.ShowDialog<bool?>(GetOwnerWindow());
            return result == true;
        };

        _viewModel.RequestVmName = async defaultName =>
        {
            RenameDialog dlg = new RenameDialog(
                defaultName,
                Res.Dialog_SnapshotName,
                Res.MyVms_Create
            );
            bool? result = await dlg.ShowDialog<bool?>(GetOwnerWindow());
            return result == true ? dlg.NewName : null;
        };

        _viewModel.RequestPushFeed = async feedNames =>
        {
            FeedPickerDialog dlg = new FeedPickerDialog(feedNames);
            bool? result = await dlg.ShowDialog<bool?>(GetOwnerWindow());
            return result == true ? dlg.SelectedIndex : -1;
        };

        _viewModel.RequestPushRepository = async repoNames =>
        {
            FeedPickerDialog dlg = new FeedPickerDialog(
                repoNames,
                title: Res.Dialog_SelectRepository,
                message: Res.Dialog_SelectRepositoryMessage,
                okText: Res.Dialog_Select
            );
            bool? result = await dlg.ShowDialog<bool?>(GetOwnerWindow());
            return result == true ? dlg.SelectedIndex : -1;
        };

        _viewModel.NavigateToMarketplaceImage = imageId =>
        {
            MainWindow mainWindow = (MainWindow)GetOwnerWindow();
            mainWindow.PendingMarketplaceImageId = imageId;
            mainWindow.NavigateToPage("Images");
        };

        _viewModel.RequestConnectionSettings = async (vmName, defaults) =>
        {
            ConnectionSettingsDialog dialog = new ConnectionSettingsDialog(vmName, defaults);
            bool? result = await dialog.ShowDialog<bool?>(GetOwnerWindow());
            if (result != true || dialog.Settings == null)
                throw new TaskCanceledException();
            return (dialog.Settings, dialog.RememberAsDefault);
        };
    }

    private Window GetOwnerWindow()
    {
        return TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException();
    }

    private void Notes_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is VmInstanceViewModel vm)
        {
            vm.Notes = tb.Text ?? "";
            _viewModel.SaveNoteForVmCommand.Execute(vm);
        }
    }

    private void FilterAll_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _viewModel.SetFilterCommand.Execute("All");
        UpdateFilterPills("All");
    }

    private void FilterRunning_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _viewModel.SetFilterCommand.Execute("Running");
        UpdateFilterPills("Running");
    }

    private void FilterStopped_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _viewModel.SetFilterCommand.Execute("Off");
        UpdateFilterPills("Off");
    }

    private void UpdateFilterPills(string active)
    {
        Avalonia.Media.IBrush? accentLight =
            this.FindResource("AccentLightBrush") as Avalonia.Media.IBrush;
        Avalonia.Media.IBrush? accentColor =
            this.FindResource("AccentBrush") as Avalonia.Media.IBrush;
        Avalonia.Media.IBrush? subtle = this.FindResource("SubtleBrush") as Avalonia.Media.IBrush;
        Avalonia.Media.IBrush? muted = this.FindResource("MutedBrush") as Avalonia.Media.IBrush;

        Border allBorder = (Border)FilterAll.Parent!;
        allBorder.Background = active == "All" ? accentLight : subtle;
        FilterAll.Foreground = active == "All" ? accentColor : muted;
        FilterAll.FontWeight =
            active == "All" ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;

        FilterRunningBorder.Background = active == "Running" ? accentLight : subtle;
        FilterRunning.Foreground = active == "Running" ? accentColor : muted;
        FilterRunning.FontWeight =
            active == "Running"
                ? Avalonia.Media.FontWeight.SemiBold
                : Avalonia.Media.FontWeight.Normal;

        FilterStoppedBorder.Background = active == "Off" ? accentLight : subtle;
        FilterStopped.Foreground = active == "Off" ? accentColor : muted;
        FilterStopped.FontWeight =
            active == "Off" ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
    }
}
