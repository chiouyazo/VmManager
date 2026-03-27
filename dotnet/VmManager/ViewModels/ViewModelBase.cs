using CommunityToolkit.Mvvm.ComponentModel;

namespace VmManager.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isError;

    protected void ShowSuccess(string message)
    {
        IsError = false;
        StatusMessage = message;
        ShowStatus = true;
    }

    protected void ShowError(string message)
    {
        IsError = true;
        StatusMessage = message;
        ShowStatus = true;
    }
}
