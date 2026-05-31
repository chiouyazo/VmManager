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
}
