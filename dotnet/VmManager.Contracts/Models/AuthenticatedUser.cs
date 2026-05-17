namespace VmManager.Contracts.Models;

public class AuthenticatedUser
{
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public HashSet<string> Permissions { get; set; } = [];
}
