using CommunityToolkit.Mvvm.ComponentModel;
using VmManager.Contracts.Models;

namespace VmManager.Views.Dialogs;

public class GrantItem : ObservableObject
{
    public string Username { get; set; } = "";

    private VmPermission _permission;
    public VmPermission Permission
    {
        get => _permission;
        set => SetProperty(ref _permission, value);
    }
}
