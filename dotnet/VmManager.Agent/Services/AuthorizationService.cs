using System.Security.Claims;

namespace VmManager.Agent.Services;

public class AuthorizationService
{
    private readonly VmOwnershipService _ownershipService;
    private readonly VmSharingService _sharingService;

    public AuthorizationService(
        VmOwnershipService ownershipService,
        VmSharingService sharingService
    )
    {
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(sharingService);
        _ownershipService = ownershipService;
        _sharingService = sharingService;
    }

    public bool HasPermission(ClaimsPrincipal user, string permission)
    {
        if (user.IsInRole("Admin"))
            return true;
        return user.HasClaim(Permission.PermissionClaimType, permission);
    }

    public bool IsOwner(ClaimsPrincipal user, string vmName)
    {
        string username = user.Identity?.Name ?? "";
        string owner = _ownershipService.GetOwner(vmName);
        return string.Equals(owner, username, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanAccessVm(ClaimsPrincipal user, string vmName, string permission)
    {
        if (user.IsInRole("Admin"))
            return true;

        string username = user.Identity?.Name ?? "";
        string owner = _ownershipService.GetOwner(vmName);

        if (string.Equals(owner, username, StringComparison.OrdinalIgnoreCase))
            return HasPermission(user, permission);

        List<VmShareEntry> shares = _sharingService.GetSharesForVm(vmName);
        VmShareEntry? share = shares.FirstOrDefault(s =>
            string.Equals(s.SharedWithUsername, username, StringComparison.OrdinalIgnoreCase)
        );

        return share != null && share.GrantedPermissions.Contains(permission);
    }

    public bool CanViewVm(ClaimsPrincipal user, string vmName)
    {
        if (user.IsInRole("Admin"))
            return true;

        if (HasPermission(user, Permission.VmViewAll))
            return true;

        string username = user.Identity?.Name ?? "";
        string owner = _ownershipService.GetOwner(vmName);
        if (string.Equals(owner, username, StringComparison.OrdinalIgnoreCase))
            return true;

        List<VmShareEntry> shares = _sharingService.GetSharesForVm(vmName);
        return shares.Any(s =>
            string.Equals(s.SharedWithUsername, username, StringComparison.OrdinalIgnoreCase)
        );
    }

    public bool IsOwnerOrAdmin(ClaimsPrincipal user, string vmName)
    {
        if (user.IsInRole("Admin"))
            return true;
        return IsOwner(user, vmName);
    }
}
