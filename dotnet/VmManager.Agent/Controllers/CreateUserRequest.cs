namespace VmManager.Agent.Controllers;

public record CreateUserRequest(
    string Username,
    string Password,
    HashSet<string> Permissions,
    bool IsAdmin
);
