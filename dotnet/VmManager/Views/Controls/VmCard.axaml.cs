using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VmManager.ViewModels;
using VmManager.Views.Dialogs;

namespace VmManager.Views.Controls;

public partial class VmCard : UserControl
{
    public VmCard()
    {
        InitializeComponent();
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && DataContext is VmInstanceViewModel vm
            && Tag is MyVmsViewModel parentVm
        )
        {
            parentVm.ToggleExpandCommand.Execute(vm);
        }
    }

    private async void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VmInstanceViewModel vm || Tag is not MyVmsViewModel parentVm)
            return;

        Window? window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        RenameDialog dialog = new RenameDialog(vm.Name);
        bool? result = await dialog.ShowDialog<bool?>(window);
        if (result != true)
            return;

        vm.PendingRename = dialog.NewName;
        parentVm.RenameVmCommand.Execute(vm);
    }

    private async void Share_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VmInstanceViewModel vm)
            return;

        Window? window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        ShareVmDialog dialog = new ShareVmDialog(vm.Name);
        await dialog.ShowDialog<object?>(window);
    }
}
