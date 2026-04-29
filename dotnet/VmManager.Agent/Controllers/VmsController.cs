using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VmsController : ControllerBase
{
    private readonly IVmBackend _backend;
    private readonly INetworkService _networkService;
    private readonly IVmTrackingService _vmTrackingService;
    private readonly NetworkTrackingService _networkTrackingService;
    private readonly SettingsService _settingsService;
    private readonly IBackgroundTaskManager _backgroundTaskManager;
    private readonly PreflightService _preflightService;
    private readonly IVmIpResolver _ipResolver;
    private readonly ILogger<VmsController> _logger;

    public VmsController(
        IVmBackend backend,
        INetworkService networkService,
        IVmTrackingService vmTrackingService,
        NetworkTrackingService networkTrackingService,
        SettingsService settingsService,
        IBackgroundTaskManager backgroundTaskManager,
        PreflightService preflightService,
        IVmIpResolver ipResolver,
        ILogger<VmsController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(networkService);
        ArgumentNullException.ThrowIfNull(vmTrackingService);
        ArgumentNullException.ThrowIfNull(networkTrackingService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(backgroundTaskManager);
        ArgumentNullException.ThrowIfNull(preflightService);
        ArgumentNullException.ThrowIfNull(ipResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _backend = backend;
        _networkService = networkService;
        _vmTrackingService = vmTrackingService;
        _networkTrackingService = networkTrackingService;
        _settingsService = settingsService;
        _backgroundTaskManager = backgroundTaskManager;
        _preflightService = preflightService;
        _ipResolver = ipResolver;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<VmInstance>), 200)]
    public async Task<IActionResult> ListVms()
    {
        List<VmInstance> allVms = new List<VmInstance>();

        try
        {
            List<VmInstance> vms = await _backend.GetVmsAsync();
            allVms.AddRange(vms);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load VMs");
        }

        Dictionary<string, VmOrigin?> managedVms = _vmTrackingService.LoadAll();
        Dictionary<string, string> notes = _vmTrackingService.LoadNotes();

        foreach (VmInstance vm in allVms)
        {
            if (notes.TryGetValue(vm.Name, out string? note))
                vm.Notes = note;
            vm.IsManaged = managedVms.ContainsKey(vm.Name);
            if (managedVms.TryGetValue(vm.Name, out VmOrigin? vmOrigin))
                vm.Origin = vmOrigin;
        }

        return Ok(allVms);
    }

    [HttpPost("{name}/start")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(typeof(object), 500)]
    public async Task<IActionResult> StartVm(string name)
    {
        _logger.LogInformation("Starting VM {VmName}", name);

        try
        {
            string? ramError = await _preflightService.CheckRamForVmAsync(name);
            if (ramError != null)
            {
                _logger.LogWarning("RAM preflight failed for {VmName}: {Error}", name, ramError);
                return BadRequest(new { error = ramError });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "RAM preflight check threw for {VmName}, proceeding anyway",
                name
            );
        }

        try
        {
            await _backend.StartVmAsync(name);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start VM {VmName}", name);
            return StatusCode(500, new { error = "Failed to start VM: " + ex.Message });
        }
    }

    [HttpPost("{name}/stop")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> StopVm(string name)
    {
        _logger.LogInformation("Stopping VM {VmName}", name);
        await _backend.StopVmAsync(name);
        return NoContent();
    }

    [HttpGet("{name}/rdp-ready")]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> IsRdpReady(string name)
    {
        string? ip = await _ipResolver.ResolveIpAsync(name);
        if (ip == null)
            return Ok(new { ready = false });

        try
        {
            using TcpClient tcp = new TcpClient();
            Task connectTask = tcp.ConnectAsync(ip, 3389);
            bool connected =
                await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask && tcp.Connected;
            return Ok(new { ready = connected });
        }
        catch
        {
            return Ok(new { ready = false });
        }
    }

    [HttpDelete("{name}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteVm(string name)
    {
        _logger.LogInformation("Deleting VM {VmName}", name);
        await _backend.DeleteVmAsync(name);
        _vmTrackingService.UntrackVm(name);
        _vmTrackingService.RemoveNote(name);

        AppSettings settings = _settingsService.Load();
        List<string> emptyNetworks = _networkTrackingService.DecrementReferences(name);
        if (settings.AutoCleanupUnusedNetworks)
        {
            foreach (string networkId in emptyNetworks)
            {
                string switchName = "VmMgr-" + networkId;
                try
                {
                    await _networkService.RemoveSwitchAsync(switchName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup switch {Switch}", switchName);
                }
                _networkTrackingService.Remove(networkId);
            }
        }

        return NoContent();
    }

    [HttpPut("{name}/rename")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RenameVm(string name, [FromBody] RenameRequest request)
    {
        _logger.LogInformation("Renaming VM {VmName} to {NewName}", name, request.NewName);
        await _backend.RenameVmAsync(name, request.NewName);

        VmOrigin? origin = _vmTrackingService.GetOrigin(name);
        if (origin != null)
        {
            _vmTrackingService.UntrackVm(name);
            _vmTrackingService.TrackVm(request.NewName, origin);
        }

        string? note = _vmTrackingService.LoadNotes().GetValueOrDefault(name);
        if (note != null)
        {
            _vmTrackingService.RemoveNote(name);
            _vmTrackingService.SaveNote(request.NewName, note);
        }

        return NoContent();
    }

    [HttpPost("{name}/reset")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetVm(string name)
    {
        _logger.LogInformation("Resetting VM {VmName} to base", name);
        bool restored = await _backend.ResetVmAsync(name);
        if (!restored)
            await _backend.ResetDiskAsync(name);
        return NoContent();
    }

    [HttpPut("{name}/notes")]
    [ProducesResponseType(204)]
    public IActionResult SaveNotes(string name, [FromBody] NotesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
            _vmTrackingService.RemoveNote(name);
        else
            _vmTrackingService.SaveNote(name, request.Notes);
        return NoContent();
    }

    [HttpPost("{name}/apply-locale")]
    [ProducesResponseType(typeof(object), 202)]
    public IActionResult ApplyLocale(string name)
    {
        _logger.LogInformation("Applying locale to VM {VmName}", name);

        AppSettings settings = _settingsService.Load();
        if (
            string.IsNullOrWhiteSpace(settings.DefaultLocale)
            && string.IsNullOrWhiteSpace(settings.DefaultTimezone)
        )
            return BadRequest(new { error = "No locale or timezone configured" });

        if (
            string.IsNullOrWhiteSpace(settings.DefaultVmUsername)
            || string.IsNullOrWhiteSpace(settings.DefaultVmPassword)
        )
            return BadRequest(
                new { error = "VM username and password must be configured in settings" }
            );

        IBackgroundTask task = _backgroundTaskManager.StartTask(
            "Applying locale to " + name,
            async ctx =>
            {
                ctx.ReportProgress(-1, "Booting VM and connecting...");
                await _backend.ConfigureLocaleAsync(
                    name,
                    settings.DefaultVmUsername,
                    settings.DefaultVmPassword,
                    settings.DefaultLocale,
                    settings.DefaultKeyboardLayout,
                    settings.DefaultTimezone
                );
                ctx.Log("Locale applied successfully");
            },
            isCancellable: false
        );

        return Accepted(new { taskId = task.Id, title = task.Title });
    }
}
