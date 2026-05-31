namespace VmManager.Contracts.Models;

public class UserAccount
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public bool IsAdmin { get; set; }
    public HashSet<string> Permissions { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public string Email { get; set; } = "";
    public int MaxVms { get; set; }
    public bool MustChangePassword { get; set; }
    public string NtHash { get; set; } = "";
}
