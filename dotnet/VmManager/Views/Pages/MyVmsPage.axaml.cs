using Avalonia.Controls;
using Avalonia.Interactivity;
using VmManager.ViewModels;
using VmManager.Views.Dialogs;
using Res = VmManager.Properties.Resources;

namespace VmManager.Views.Pages;

public partial class MyVmsPage : UserControl
{
    private readonly MyVmsViewModel _viewModel;

    public MyVmsPage(MyVmsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.ConfirmAction = async (_, _) =>
        {
            // TODO: replace with proper Avalonia confirm dialog
            return true;
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
    }

    private Window GetOwnerWindow()
    {
        return TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException();
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
                btn.Content = "Done";
                await Task.Delay(1200);
                btn.Content = "Copy";
            }
        }
        catch { }
    }

    private async void CopyCredential_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string text)
            return;
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
                await topLevel.Clipboard.SetTextAsync(text);
            object? original = btn.Content;
            btn.Content = "Done";
            await Task.Delay(1200);
            btn.Content = original;
        }
        catch { }
    }

    private async void RenameButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not VmInstanceViewModel vm)
            return;

        RenameDialog dialog = new RenameDialog(vm.Name);
        bool? result = await dialog.ShowDialog<bool?>(GetOwnerWindow());
        if (result != true)
            return;

        vm.PendingRename = dialog.NewName;
        _viewModel.RenameVm(vm);
    }

    private void NoteBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is VmInstanceViewModel vm)
        {
            vm.Notes = tb.Text ?? "";
            _viewModel.SaveNoteForVmCommand.Execute(vm);
        }
    }

    private void SnapshotExpander_Expanded(object? sender, RoutedEventArgs e)
    {
        if (
            sender is Expander expander
            && expander.DataContext is VmInstanceViewModel vm
            && !vm.SnapshotsLoaded
        )
            _ = _viewModel.LoadSnapshotsForVmAsync(vm);
    }
}
