using VmManager.Contracts.Models;

namespace VmManager.Agent.Controllers;

public class VmAccessGrantRequest
{
    public VmPermission Permission { get; set; }
}
