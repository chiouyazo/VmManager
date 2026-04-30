using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services;

public class VmAuthorizationService
{
    private readonly VmAccessStore _accessStore;
    private readonly SettingsService _settingsService;

    public VmAuthorizationService(VmAccessStore accessStore, SettingsService settingsService)
    {
        _accessStore = accessStore;
        _settingsService = settingsService;
    }

    public bool IsAccessControlEnabled()
    {
        return _settingsService.Load().AccessControlEnabled;
    }

    public bool IsAdmin(string? username)
    {
        if (username == null)
            return true;
        if (!IsAccessControlEnabled())
            return true;
        AppSettings settings = _settingsService.Load();
        return !string.IsNullOrEmpty(settings.AdminUser)
            && settings.AdminUser.Equals(username, StringComparison.OrdinalIgnoreCase);
    }

    public VmPermission? GetEffectivePermission(string? username, string vmName)
    {
        if (IsAdmin(username))
            return VmPermission.Manage;

        string? owner = _accessStore.GetOwner(vmName);
        if (
            owner != null
            && username != null
            && owner.Equals(username, StringComparison.OrdinalIgnoreCase)
        )
            return VmPermission.Manage;

        if (username == null)
            return null;

        VmAccessEntry? entry = _accessStore.GetEntry(vmName);
        VmAccessGrant? grant = entry?.Grants.FirstOrDefault(g =>
            g.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
        );
        return grant?.Permission;
    }

    public bool CanPerform(string? username, string vmName, VmPermission required)
    {
        VmPermission? effective = GetEffectivePermission(username, vmName);
        return effective != null && effective.Value >= required;
    }

    public bool CanDelete(string? username, string vmName)
    {
        if (IsAdmin(username))
            return true;
        string? owner = _accessStore.GetOwner(vmName);
        return owner != null
            && username != null
            && owner.Equals(username, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanManageAccess(string? username, string vmName)
    {
        return CanDelete(username, vmName);
    }

    public bool CanView(string? username, string vmName)
    {
        return GetEffectivePermission(username, vmName) != null;
    }
}
