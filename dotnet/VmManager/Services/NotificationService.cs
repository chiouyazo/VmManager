using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace VmManager.Services;

public class NotificationService
{
    private WindowNotificationManager? _manager;

    public void SetManager(WindowNotificationManager manager)
    {
        _manager = manager;
    }

    public void ShowSuccess(string message)
    {
        Show(message, NotificationType.Success, TimeSpan.FromSeconds(4));
    }

    public void ShowError(string message)
    {
        Show(message, NotificationType.Error, TimeSpan.Zero);
    }

    public void ShowWarning(string message)
    {
        Show(message, NotificationType.Warning, TimeSpan.FromSeconds(6));
    }

    public void ShowInfo(string message)
    {
        Show(message, NotificationType.Information, TimeSpan.FromSeconds(4));
    }

    private void Show(string message, NotificationType type, TimeSpan expiration)
    {
        if (_manager == null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _manager.Show(new Notification("", message, type, expiration));
        });
    }
}
