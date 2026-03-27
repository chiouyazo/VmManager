using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VmManager.ViewModels;
using VmManager.Views.Dialogs;
using Res = VmManager.Properties.Resources;

namespace VmManager.Views.Pages;

public partial class ImagesPage : UserControl
{
    private readonly ImagesViewModel _viewModel;

    public ImagesPage(ImagesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.RequestVmName = async defaultName =>
        {
            RenameDialog dialog = new RenameDialog(
                defaultName,
                Res.Dialog_NameYourVm,
                Res.MyVms_Create
            );
            bool? result = await dialog.ShowDialog<bool?>(
                TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException()
            );
            return result == true ? dialog.NewName : null;
        };

        _viewModel.NavigateTo = tag =>
        {
            if (TopLevel.GetTopLevel(this) is MainWindow mw)
                mw.NavigateToPage(tag);
        };

        _viewModel.NavigateWithMessage = (tag, message) =>
        {
            if (TopLevel.GetTopLevel(this) is MainWindow mw)
            {
                mw.PendingMyVmsMessage = message;
                mw.NavigateToPage(tag);
            }
        };
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

    private void FeatureTag_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is string feature)
            _viewModel.ToggleFeatureFilterCommand.Execute(feature);
    }

    private void ActiveFilter_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is string feature)
            _viewModel.ToggleFeatureFilterCommand.Execute(feature);
    }
}
