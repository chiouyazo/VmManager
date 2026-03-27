using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/vms/{vmName}/snapshots")]
public class SnapshotsController : ControllerBase
{
    private readonly IVmBackend _backend;
    private readonly IVmTrackingService _vmTrackingService;
    private readonly SnapshotPushService _snapshotPushService;
    private readonly FeedResolutionService _feedResolutionService;
    private readonly SettingsService _settingsService;
    private readonly IBackgroundTaskManager _backgroundTaskManager;
    private readonly ILogger<SnapshotsController> _logger;

    public SnapshotsController(
        IVmBackend backend,
        IVmTrackingService vmTrackingService,
        SnapshotPushService snapshotPushService,
        FeedResolutionService feedResolutionService,
        SettingsService settingsService,
        IBackgroundTaskManager backgroundTaskManager,
        ILogger<SnapshotsController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(vmTrackingService);
        ArgumentNullException.ThrowIfNull(snapshotPushService);
        ArgumentNullException.ThrowIfNull(feedResolutionService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(backgroundTaskManager);
        ArgumentNullException.ThrowIfNull(logger);
        _backend = backend;
        _vmTrackingService = vmTrackingService;
        _snapshotPushService = snapshotPushService;
        _feedResolutionService = feedResolutionService;
        _settingsService = settingsService;
        _backgroundTaskManager = backgroundTaskManager;
        _logger = logger;
    }

    /// <summary>List all snapshots for a VM.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<VmSnapshot>), 200)]
    public async Task<IActionResult> ListSnapshots(string vmName)
    {
        List<VmSnapshot> snapshots = await _backend.GetSnapshotsAsync(vmName);
        return Ok(snapshots);
    }

    /// <summary>Create a named snapshot for a VM.</summary>
    [HttpPost]
    [ProducesResponseType(204)]
    public async Task<IActionResult> CreateSnapshot(
        string vmName,
        [FromBody] CreateSnapshotRequest request
    )
    {
        _logger.LogInformation(
            "Creating snapshot {SnapshotName} for VM {VmName}",
            request.Name,
            vmName
        );
        await _backend.CreateSnapshotAsync(vmName, request.Name);
        return NoContent();
    }

    /// <summary>Restore a snapshot. Stops the VM if running before restoring.</summary>
    [HttpPost("{snapshotId}/restore")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RestoreSnapshot(string vmName, string snapshotId)
    {
        _logger.LogInformation(
            "Restoring snapshot {SnapshotId} for VM {VmName}",
            snapshotId,
            vmName
        );
        await _backend.RestoreSnapshotAsync(vmName, snapshotId);
        return NoContent();
    }

    /// <summary>Delete a snapshot.</summary>
    [HttpDelete("{snapshotId}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteSnapshot(string vmName, string snapshotId)
    {
        _logger.LogInformation(
            "Deleting snapshot {SnapshotId} for VM {VmName}",
            snapshotId,
            vmName
        );
        await _backend.DeleteSnapshotAsync(vmName, snapshotId);
        return NoContent();
    }

    /// <summary>Clone a new VM from a snapshot.</summary>
    [HttpPost("{snapshotId}/clone")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> CloneFromSnapshot(
        string vmName,
        string snapshotId,
        [FromBody] CloneRequest request
    )
    {
        _logger.LogInformation(
            "Cloning VM {VmName} from snapshot {SnapshotId} as {NewName}",
            vmName,
            snapshotId,
            request.NewName
        );

        List<VmSnapshot> snapshots = await _backend.GetSnapshotsAsync(vmName);
        VmSnapshot? snapshot = snapshots.FirstOrDefault(s => s.Id == snapshotId);
        if (snapshot == null)
            return NotFound(new { error = "Snapshot not found" });

        await _backend.CloneVmFromSnapshotAsync(vmName, snapshot.Name, request.NewName);
        _vmTrackingService.TrackVm(request.NewName, _vmTrackingService.GetOrigin(vmName));
        return NoContent();
    }

    /// <summary>Push a snapshot to a feed. Runs as a background task. Optionally specify feedId to override auto-resolution.</summary>
    [HttpPost("{snapshotId}/push")]
    [ProducesResponseType(typeof(object), 202)]
    public IActionResult PushSnapshot(
        string vmName,
        string snapshotId,
        [FromBody] PushRequest? request = null
    )
    {
        _logger.LogInformation("Pushing snapshot {SnapshotId} for VM {VmName}", snapshotId, vmName);

        AppSettings settings = _settingsService.Load();
        VmOrigin? origin = _vmTrackingService.GetOrigin(vmName);

        FeedConfiguration? targetFeed =
            request?.FeedId != null
                ? settings.Feeds.FirstOrDefault(f => f.Id == request.FeedId)
                : _feedResolutionService.ResolvePushFeed(origin, settings);

        if (targetFeed == null)
            return BadRequest(
                new { error = "No push target feed resolved. Provide feedId in request body." }
            );

        List<VmSnapshot> snapshots = _backend.GetSnapshotsAsync(vmName).GetAwaiter().GetResult();
        VmSnapshot? snapshot = snapshots.FirstOrDefault(s => s.Id == snapshotId);
        if (snapshot == null)
            return NotFound(new { error = "Snapshot not found" });

        FeedConfiguration feed = targetFeed;
        IBackgroundTask task = _backgroundTaskManager.StartTask(
            "Pushing " + snapshot.Name + " to " + feed.Name,
            async ctx =>
            {
                ctx.ReportProgress(0, "Pushing to " + feed.Name);
                await _snapshotPushService.PushAsync(
                    feed,
                    vmName,
                    snapshot.Name,
                    snapshot.Id,
                    origin,
                    new Progress<PushProgress>(p =>
                    {
                        ctx.ReportProgress(p.Percent / 100.0, p.Phase);
                    }),
                    ctx.Token
                );
                ctx.Log("Push complete");
            }
        );

        return Accepted(new { taskId = task.Id, title = task.Title });
    }
}
