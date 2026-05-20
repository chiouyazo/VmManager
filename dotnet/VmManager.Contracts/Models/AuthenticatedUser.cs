namespace VmManager.Contracts.Models;

public class AuthenticatedUser
{
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public HashSet<string> Permissions { get; set; } = [];
    public string Email { get; set; } = "";
    public int MaxVms { get; set; }
    public bool MustChangePassword { get; set; }
}
