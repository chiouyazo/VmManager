namespace VmManager.Contracts.Models;

public class VmAccessGrant
{
    public string Username { get; set; } = "";
    public VmPermission Permission { get; set; }
}
