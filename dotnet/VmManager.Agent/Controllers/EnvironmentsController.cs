using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/environments")]
[Authorize(Policy = Permission.TestEnvManage)]
public class EnvironmentsController : ControllerBase
{
    private readonly EnvironmentService _environments;

    public EnvironmentsController(EnvironmentService environments)
    {
        _environments = environments;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<EnvironmentView>), 200)]
    public async Task<IActionResult> List() => Ok(await _environments.ListAsync());

    [HttpGet("{key}")]
    [ProducesResponseType(typeof(EnvironmentView), 200)]
    public async Task<IActionResult> Get(string key)
    {
        EnvironmentView? view = await _environments.GetAsync(key);
        return view == null ? NotFound() : Ok(view);
    }

    [HttpGet("{key}/log")]
    public IActionResult GetLog(string key)
    {
        string? log = _environments.GetLogText(key);
        return log == null ? NotFound() : Content(log, "text/plain");
    }

    [HttpPost]
    [ProducesResponseType(typeof(object), 202)]
    public async Task<IActionResult> Provision([FromBody] EnvironmentProvisionRequest request)
    {
        string caller = User.Identity?.Name ?? "admin";
        ProvisionOutcome outcome = await _environments.ProvisionAsync(request, caller);
        return outcome.Status switch
        {
            ProvisionStatus.Accepted => Accepted(
                new { key = outcome.Key, taskId = outcome.TaskId }
            ),
            ProvisionStatus.Reused => Ok(new { key = outcome.Key, reused = true }),
            ProvisionStatus.Conflict => Conflict(new { error = outcome.Message }),
            _ => BadRequest(new { error = outcome.Message }),
        };
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key) =>
        await _environments.DeleteAsync(key) ? NoContent() : NotFound();

    [HttpPost("{key}/extend")]
    public IActionResult Extend(string key, [FromBody] ExtendRequest body)
    {
        DateTime? expiresAt = _environments.Extend(key, body.Minutes);
        return expiresAt == null ? NotFound() : Ok(new { key, expiresAt });
    }

    public sealed class ExtendRequest
    {
        public int Minutes { get; set; }
    }
}
