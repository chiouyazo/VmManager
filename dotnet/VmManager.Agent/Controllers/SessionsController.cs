using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permission.UsersManage)]
public class SessionsController : ControllerBase
{
    private readonly RdpSessionStore _sessionStore;
    private readonly IVmBackend _backend;

    public SessionsController(RdpSessionStore sessionStore, IVmBackend backend)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(backend);
        _sessionStore = sessionStore;
        _backend = backend;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<VmSessionGroup>), 200)]
    public async Task<IActionResult> GetSessions()
    {
        IReadOnlyList<RdpSession> allSessions = _sessionStore.GetAllSessions();
        List<VmInstance> allVms = await _backend.GetVmsAsync();

        Dictionary<string, string> vmStates = allVms.ToDictionary(
            v => v.Name,
            v => v.State,
            StringComparer.OrdinalIgnoreCase
        );

        List<VmSessionGroup> groups = allSessions
            .Where(s => s.State == RdpSessionState.Active || s.State == RdpSessionState.Pending)
            .GroupBy(s => s.VmName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new VmSessionGroup
            {
                VmName = g.Key,
                VmState = vmStates.GetValueOrDefault(g.Key, "Unknown"),
                Sessions = g.Select(s => new ActiveSession
                    {
                        VmName = s.VmName,
                        Token = s.Token,
                        Username = s.Username,
                        ConnectedAt = s.CreatedAt,
                        State = s.State.ToString(),
                        DurationSeconds = (DateTimeOffset.UtcNow - s.CreatedAt).TotalSeconds,
                    })
                    .ToList(),
            })
            .ToList();

        return Ok(groups);
    }

    [HttpPost("{vmName}/{token}/disconnect")]
    [ProducesResponseType(204)]
    public IActionResult DisconnectSession(string vmName, string token)
    {
        _sessionStore.ForceDisconnect(token);
        return NoContent();
    }
}
