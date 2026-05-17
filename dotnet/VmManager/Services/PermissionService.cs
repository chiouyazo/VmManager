using VmManager.Contracts.Models;

namespace VmManager.Services;

public class PermissionService
{
    private AuthenticatedUser? _currentUser;

    public string Username => _currentUser?.Username ?? "";
    public bool IsAdmin => _currentUser?.IsAdmin ?? false;
    public bool IsLoggedIn => _currentUser != null;

    public async Task RefreshAsync()
    {
        if (App.AgentClient == null)
        {
            _currentUser = null;
            return;
        }

        try
        {
            _currentUser = await App.AgentClient.GetCurrentUserAsync();
        }
        catch
        {
            _currentUser = null;
        }
    }

    public void Clear()
    {
        _currentUser = null;
    }

    public bool HasPermission(string permission)
    {
        if (_currentUser == null)
            return false;
        if (_currentUser.IsAdmin)
            return true;
        return _currentUser.Permissions.Contains(permission);
    }

    public bool CanSeeMarketplace => HasPermission(Permission.CatalogBrowse);
    public bool CanSeeSettings =>
        HasPermission(Permission.SettingsView)
        || HasPermission(Permission.SettingsManageFeeds)
        || HasPermission(Permission.SettingsEditVmDefaults)
        || HasPermission(Permission.SettingsEditScripts);
    public bool CanManageFeeds => HasPermission(Permission.SettingsManageFeeds);
    public bool CanEditVmDefaults => HasPermission(Permission.SettingsEditVmDefaults);
    public bool CanEditScripts => HasPermission(Permission.SettingsEditScripts);
    public bool CanCreateVm => HasPermission(Permission.VmCreate);
    public bool CanImportImages => HasPermission(Permission.CatalogImport);
    public bool CanDeleteLocalImages => HasPermission(Permission.CatalogDeleteLocal);
}
