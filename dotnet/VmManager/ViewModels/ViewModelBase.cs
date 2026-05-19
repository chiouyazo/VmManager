using CommunityToolkit.Mvvm.ComponentModel;
using VmManager.Services;

namespace VmManager.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isError;

    public NotificationService? Notifications { get; set; }

    protected void ShowSuccess(string message)
    {
        IsError = false;
        StatusMessage = message;
        ShowStatus = true;
        Notifications?.ShowSuccess(message);
    }

    protected void ShowError(string message)
    {
        IsError = true;
        StatusMessage = message;
        ShowStatus = true;
        Notifications?.ShowError(message);
    }
}
