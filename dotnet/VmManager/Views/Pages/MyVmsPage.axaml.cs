using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    private async void SessionsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not VmInstanceViewModel vm)
            return;

        StackPanel panel = new StackPanel { MinWidth = 300 };

        ProgressBar loader = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 3,
            Margin = new Avalonia.Thickness(8),
        };
        panel.Children.Add(loader);

        Flyout flyout = new Flyout
        {
            Content = panel,
            Placement = PlacementMode.BottomEdgeAlignedRight,
        };
        flyout.ShowAt(btn);

        await _viewModel.LoadShadowSessionsAsync(vm);

        panel.Children.Clear();

        if (vm.ShadowSessions.Count == 0)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = "No active sessions",
                    Opacity = 0.6,
                    Margin = new Avalonia.Thickness(8),
                }
            );
        }
        else
        {
            foreach (VmManager.Contracts.Models.RdpShadowSession session in vm.ShadowSessions)
            {
                DockPanel row = new DockPanel { Margin = new Avalonia.Thickness(0, 2) };

                Button forceBtn = new Button
                {
                    Content = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.Important,
                        FontSize = 12,
                    },
                    Padding = new Avalonia.Thickness(6, 4),
                    Background = Avalonia.Media.Brushes.Transparent,
                    [DockPanel.DockProperty] = Avalonia.Controls.Dock.Right,
                };
                Avalonia.Controls.ToolTip.SetTip(forceBtn, "Shadow without consent");
                VmManager.Contracts.Models.RdpShadowSession capturedSession = session;
                forceBtn.Click += (_, _) => _viewModel.LaunchShadow(vm, capturedSession, true);
                row.Children.Add(forceBtn);

                Button entryBtn = new Button
                {
                    Content =
                        session.SessionName
                        + "  "
                        + session.Username
                        + "  (ID: "
                        + session.SessionId
                        + ")",
                    Background = Avalonia.Media.Brushes.Transparent,
                    Padding = new Avalonia.Thickness(8, 6),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    FontSize = 12,
                };
                entryBtn.Click += (_, _) => _viewModel.LaunchShadow(vm, capturedSession, false);
                row.Children.Add(entryBtn);

                panel.Children.Add(row);
            }
        }
    }

    private async void ShareButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not VmInstanceViewModel vm)
            return;

        ShareVmDialog dialog = new ShareVmDialog(vm.Name);
        await dialog.ShowDialog<object?>(GetOwnerWindow());
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
