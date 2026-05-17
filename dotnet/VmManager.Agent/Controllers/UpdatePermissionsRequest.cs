namespace VmManager.Agent.Controllers;

public record UpdatePermissionsRequest(HashSet<string> Permissions, bool? IsAdmin);
