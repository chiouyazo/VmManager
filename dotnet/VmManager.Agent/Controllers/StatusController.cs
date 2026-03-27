using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly IVmBackend _vmBackend;

    public StatusController(IVmBackend vmBackend)
    {
        ArgumentNullException.ThrowIfNull(vmBackend);
        _vmBackend = vmBackend;
    }

    /// <summary>Get agent health status and version.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(object), 200)]
    public IActionResult GetStatus()
    {
        string version =
            Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "unknown";

        string backend = OperatingSystem.IsWindows() ? "HyperV" : "KVM";

        return Ok(
            new
            {
                status = "healthy",
                version,
                backend,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
    }

    [HttpGet("troubleshoot")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> Troubleshoot()
    {
        string? report = await _vmBackend.TroubleshootAsync();
        if (report == null)
            return Ok(new { report = "Troubleshooting is not available for this backend." });

        return Ok(new { report });
    }

    /// <summary>Get the count of currently active RDP sessions.</summary>
    [HttpGet("rdp-sessions/active-count")]
    [ProducesResponseType(typeof(object), 200)]
    public IActionResult GetActiveRdpSessionCount([FromServices] RdpSessionStore sessionStore)
    {
        int count = sessionStore.GetAllSessions().Count(s => s.State == RdpSessionState.Active);
        return Ok(new { count });
    }

    /// <summary>Cancel a running background task by its ID.</summary>
    [HttpPost("tasks/{taskId}/cancel")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult CancelTask(string taskId, [FromServices] BackgroundTaskManager taskManager)
    {
        IBackgroundTask? task = taskManager.GetAllTasks().FirstOrDefault(t => t.Id == taskId);
        if (task == null)
            return NotFound(new { error = "Task not found" });

        task.Cancel();
        return NoContent();
    }

    /// <summary>List all background tasks with their current progress and status.</summary>
    [HttpGet("tasks")]
    [ProducesResponseType(typeof(object), 200)]
    public IActionResult GetTasks([FromServices] BackgroundTaskManager taskManager)
    {
        IEnumerable<IBackgroundTask> tasks = taskManager.GetAllTasks();
        return Ok(
            tasks.Select(t => new
            {
                t.Id,
                t.Title,
                t.Status,
                t.Progress,
                t.IsComplete,
                t.IsFailed,
                t.IsCancelled,
                t.ErrorMessage,
            })
        );
    }
}
