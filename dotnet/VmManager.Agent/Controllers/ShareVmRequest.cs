namespace VmManager.Agent.Controllers;

public record ShareVmRequest(string Username, HashSet<string> Permissions);
