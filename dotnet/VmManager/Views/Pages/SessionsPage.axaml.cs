using Avalonia.Controls;
using VmManager.Services;
using VmManager.ViewModels;

namespace VmManager.Views.Pages;

public partial class SessionsPage : UserControl
{
    private readonly SessionsViewModel? _viewModel;

    public SessionsPage()
    {
        InitializeComponent();
    }

    public SessionsPage(SessionsViewModel viewModel, NotificationService notificationService)
    {
        _viewModel = viewModel;
        _viewModel.Notifications = notificationService;
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel?.StartPolling();
        if (_viewModel != null)
            _ = _viewModel.RefreshAsync();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _viewModel?.StopPolling();
        base.OnDetachedFromVisualTree(e);
    }
}
