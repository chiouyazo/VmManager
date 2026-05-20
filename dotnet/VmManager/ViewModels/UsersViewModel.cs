using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.ViewModels;

public partial class UsersViewModel : ViewModelBase
{
    private readonly ILogger<UsersViewModel> _logger;

    public UsersViewModel(ILogger<UsersViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public ObservableCollection<AuthenticatedUser> Users { get; } = [];

    [ObservableProperty]
    private AuthenticatedUser? _selectedUser;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private bool _isCreateMode;

    [ObservableProperty]
    private string _editUsername = "";

    [ObservableProperty]
    private string _newUsername = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private bool _editIsAdmin;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _resetPasswordText = "";

    [ObservableProperty]
    private bool _showResetPassword;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private int _editMaxVms;

    [ObservableProperty]
    private bool _permVmCreate;

    [ObservableProperty]
    private bool _permVmDelete;

    [ObservableProperty]
    private bool _permVmStart;

    [ObservableProperty]
    private bool _permVmStop;

    [ObservableProperty]
    private bool _permVmRename;

    [ObservableProperty]
    private bool _permVmReset;

    [ObservableProperty]
    private bool _permVmApplyLocale;

    [ObservableProperty]
    private bool _permVmViewAll;

    [ObservableProperty]
    private bool _permSnapshotCreate;

    [ObservableProperty]
    private bool _permSnapshotRestore;

    [ObservableProperty]
    private bool _permSnapshotDelete;

    [ObservableProperty]
    private bool _permSnapshotClone;

    [ObservableProperty]
    private bool _permSnapshotPush;

    [ObservableProperty]
    private bool _permCatalogBrowse;

    [ObservableProperty]
    private bool _permCatalogImport;

    [ObservableProperty]
    private bool _permCatalogDeleteLocal;

    [ObservableProperty]
    private bool _permSettingsView;

    [ObservableProperty]
    private bool _permSettingsEditVmDefaults;

    [ObservableProperty]
    private bool _permSettingsManageFeeds;

    [ObservableProperty]
    private bool _permSettingsEditScripts;

    [ObservableProperty]
    private bool _permRdpConnect;

    [ObservableProperty]
    private bool _permUsersManage;

    public async Task LoadUsersAsync()
    {
        if (App.AgentClient == null)
            return;

        IsLoading = true;
        try
        {
            List<AuthenticatedUser> users = await App.AgentClient.GetUsersAsync();
            Users.Clear();
            foreach (AuthenticatedUser user in users)
                Users.Add(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
            ShowError("Failed to load users: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SelectUser(AuthenticatedUser user)
    {
        SelectedUser = user;
        IsCreateMode = false;
        ShowResetPassword = false;
        ShowDeleteConfirmation = false;
        ResetPasswordText = "";

        if (user.IsAdmin)
        {
            IsEditMode = false;
            return;
        }

        IsEditMode = true;
        EditUsername = user.Username;
        EditIsAdmin = user.IsAdmin;
        EditMaxVms = user.MaxVms;
        LoadPermissionsFromUser(user);
    }

    [RelayCommand]
    private void EnterCreateMode()
    {
        SelectedUser = null;
        IsEditMode = false;
        IsCreateMode = true;
        ShowResetPassword = false;
        NewUsername = "";
        NewPassword = "";
        EditIsAdmin = false;
        ClearPermissionCheckboxes();
    }

    [RelayCommand]
    private void ToggleResetPassword()
    {
        ShowResetPassword = !ShowResetPassword;
        if (!ShowResetPassword)
            ResetPasswordText = "";
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        if (
            App.AgentClient == null
            || string.IsNullOrWhiteSpace(NewUsername)
            || string.IsNullOrWhiteSpace(NewPassword)
        )
            return;

        IsBusy = true;
        try
        {
            HashSet<string> permissions = CollectPermissions();
            await App.AgentClient.CreateUserAsync(
                NewUsername,
                NewPassword,
                permissions,
                EditIsAdmin
            );
            ShowSuccess("User created successfully");
            IsCreateMode = false;
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            ShowError("Failed to create user: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        if (App.AgentClient == null || SelectedUser == null)
            return;

        IsBusy = true;
        try
        {
            if (
                !string.Equals(EditUsername, SelectedUser.Username, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(EditUsername)
            )
            {
                await App.AgentClient.RenameUserAsync(SelectedUser.Username, EditUsername);
            }

            HashSet<string> permissions = CollectPermissions();
            await App.AgentClient.UpdateUserPermissionsAsync(
                EditUsername,
                permissions,
                EditIsAdmin
            );

            if (SelectedUser.MaxVms != EditMaxVms)
                await App.AgentClient.SetUserQuotaAsync(EditUsername, EditMaxVms);

            ShowSuccess("User saved");
            await LoadUsersAsync();

            AuthenticatedUser? updated = Users.FirstOrDefault(u =>
                string.Equals(u.Username, EditUsername, StringComparison.OrdinalIgnoreCase)
            );
            if (updated != null)
                SelectUser(updated);
        }
        catch (Exception ex)
        {
            ShowError("Failed to save user: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DeleteUser()
    {
        if (SelectedUser == null || SelectedUser.IsAdmin)
            return;

        ShowDeleteConfirmation = true;
        ShowResetPassword = false;
        IsEditMode = false;
        IsCreateMode = false;
    }

    [RelayCommand]
    private async Task SendInviteAsync(AuthenticatedUser user)
    {
        if (App.AgentClient == null)
            return;

        try
        {
            await App.AgentClient.SendInviteEmailAsync(user.Username);
            ShowSuccess("Invite email sent to " + user.Username);
        }
        catch (Exception ex)
        {
            ShowError("Failed to send invite: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task ConfirmDeleteUserAsync()
    {
        if (App.AgentClient == null || SelectedUser == null)
            return;

        IsBusy = true;
        try
        {
            await App.AgentClient.DeleteUserAsync(SelectedUser.Username);
            ShowSuccess("User deleted");
            SelectedUser = null;
            IsEditMode = false;
            ShowDeleteConfirmation = false;
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            ShowError("Failed to delete user: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (
            App.AgentClient == null
            || SelectedUser == null
            || string.IsNullOrWhiteSpace(ResetPasswordText)
        )
            return;

        IsBusy = true;
        try
        {
            await App.AgentClient.ResetUserPasswordAsync(SelectedUser.Username, ResetPasswordText);
            ResetPasswordText = "";
            ShowResetPassword = false;
            ShowSuccess("Password reset successfully");
        }
        catch (Exception ex)
        {
            ShowError("Failed to reset password: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadPermissionsFromUser(AuthenticatedUser user)
    {
        PermVmCreate = user.Permissions.Contains(Permission.VmCreate);
        PermVmDelete = user.Permissions.Contains(Permission.VmDelete);
        PermVmStart = user.Permissions.Contains(Permission.VmStart);
        PermVmStop = user.Permissions.Contains(Permission.VmStop);
        PermVmRename = user.Permissions.Contains(Permission.VmRename);
        PermVmReset = user.Permissions.Contains(Permission.VmReset);
        PermVmApplyLocale = user.Permissions.Contains(Permission.VmApplyLocale);
        PermVmViewAll = user.Permissions.Contains(Permission.VmViewAll);
        PermSnapshotCreate = user.Permissions.Contains(Permission.SnapshotCreate);
        PermSnapshotRestore = user.Permissions.Contains(Permission.SnapshotRestore);
        PermSnapshotDelete = user.Permissions.Contains(Permission.SnapshotDelete);
        PermSnapshotClone = user.Permissions.Contains(Permission.SnapshotClone);
        PermSnapshotPush = user.Permissions.Contains(Permission.SnapshotPush);
        PermCatalogBrowse = user.Permissions.Contains(Permission.CatalogBrowse);
        PermCatalogImport = user.Permissions.Contains(Permission.CatalogImport);
        PermCatalogDeleteLocal = user.Permissions.Contains(Permission.CatalogDeleteLocal);
        PermSettingsView = user.Permissions.Contains(Permission.SettingsView);
        PermSettingsEditVmDefaults = user.Permissions.Contains(Permission.SettingsEditVmDefaults);
        PermSettingsManageFeeds = user.Permissions.Contains(Permission.SettingsManageFeeds);
        PermSettingsEditScripts = user.Permissions.Contains(Permission.SettingsEditScripts);
        PermRdpConnect = user.Permissions.Contains(Permission.RdpConnect);
        PermUsersManage = user.Permissions.Contains(Permission.UsersManage);
    }

    private HashSet<string> CollectPermissions()
    {
        HashSet<string> permissions = [];
        if (PermVmCreate)
            permissions.Add(Permission.VmCreate);
        if (PermVmDelete)
            permissions.Add(Permission.VmDelete);
        if (PermVmStart)
            permissions.Add(Permission.VmStart);
        if (PermVmStop)
            permissions.Add(Permission.VmStop);
        if (PermVmRename)
            permissions.Add(Permission.VmRename);
        if (PermVmReset)
            permissions.Add(Permission.VmReset);
        if (PermVmApplyLocale)
            permissions.Add(Permission.VmApplyLocale);
        if (PermVmViewAll)
            permissions.Add(Permission.VmViewAll);
        if (PermSnapshotCreate)
            permissions.Add(Permission.SnapshotCreate);
        if (PermSnapshotRestore)
            permissions.Add(Permission.SnapshotRestore);
        if (PermSnapshotDelete)
            permissions.Add(Permission.SnapshotDelete);
        if (PermSnapshotClone)
            permissions.Add(Permission.SnapshotClone);
        if (PermSnapshotPush)
            permissions.Add(Permission.SnapshotPush);
        if (PermCatalogBrowse)
            permissions.Add(Permission.CatalogBrowse);
        if (PermCatalogImport)
            permissions.Add(Permission.CatalogImport);
        if (PermCatalogDeleteLocal)
            permissions.Add(Permission.CatalogDeleteLocal);
        if (PermSettingsView)
            permissions.Add(Permission.SettingsView);
        if (PermSettingsEditVmDefaults)
            permissions.Add(Permission.SettingsEditVmDefaults);
        if (PermSettingsManageFeeds)
            permissions.Add(Permission.SettingsManageFeeds);
        if (PermSettingsEditScripts)
            permissions.Add(Permission.SettingsEditScripts);
        if (PermRdpConnect)
            permissions.Add(Permission.RdpConnect);
        if (PermUsersManage)
            permissions.Add(Permission.UsersManage);
        permissions.Add(Permission.VmViewOwn);
        return permissions;
    }

    private void ClearPermissionCheckboxes()
    {
        PermVmCreate = false;
        PermVmDelete = false;
        PermVmStart = false;
        PermVmStop = false;
        PermVmRename = false;
        PermVmReset = false;
        PermVmApplyLocale = false;
        PermVmViewAll = false;
        PermSnapshotCreate = false;
        PermSnapshotRestore = false;
        PermSnapshotDelete = false;
        PermSnapshotClone = false;
        PermSnapshotPush = false;
        PermCatalogBrowse = false;
        PermCatalogImport = false;
        PermCatalogDeleteLocal = false;
        PermSettingsView = false;
        PermSettingsEditVmDefaults = false;
        PermSettingsManageFeeds = false;
        PermSettingsEditScripts = false;
        PermRdpConnect = false;
        PermUsersManage = false;
    }
}
