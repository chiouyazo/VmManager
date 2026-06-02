using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;
using VmManager.Agent.Services.Monitoring;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Controllers;

/// <summary>Monitoring and metrics endpoints.</summary>
[ApiController]
[Route("api/monitoring")]
[Authorize]
public sealed class MonitoringController : ControllerBase
{
    private readonly AlertStore _alertStore;
    private readonly MetricsCache _metricsCache;
    private readonly MonitoringService _monitoringService;
    private readonly SettingsService _settingsService;

    public MonitoringController(
        AlertStore alertStore,
        MetricsCache metricsCache,
        MonitoringService monitoringService,
        SettingsService settingsService
    )
    {
        ArgumentNullException.ThrowIfNull(alertStore);
        ArgumentNullException.ThrowIfNull(metricsCache);
        ArgumentNullException.ThrowIfNull(monitoringService);
        ArgumentNullException.ThrowIfNull(settingsService);
        _alertStore = alertStore;
        _metricsCache = metricsCache;
        _monitoringService = monitoringService;
        _settingsService = settingsService;
    }

    /// <summary>List alerts with optional filters.</summary>
    [HttpGet("alerts")]
    [ProducesResponseType(typeof(List<MonitoringAlert>), 200)]
    public IActionResult GetAlerts(
        [FromQuery] AlertSeverity? severity = null,
        [FromQuery] string? vmName = null,
        [FromQuery] DateTimeOffset? since = null,
        [FromQuery] bool? acknowledged = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0
    )
    {
        List<MonitoringAlert> alerts = _alertStore.Query(
            severity,
            vmName,
            since,
            acknowledged,
            limit,
            offset
        );
        return Ok(alerts);
    }

    /// <summary>Get a single alert by ID.</summary>
    [HttpGet("alerts/{id}")]
    [ProducesResponseType(typeof(MonitoringAlert), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetAlert(string id)
    {
        MonitoringAlert? alert = _alertStore.GetById(id);
        if (alert == null)
            return NotFound();
        return Ok(alert);
    }

    /// <summary>Acknowledge an alert.</summary>
    [HttpPost("alerts/{id}/acknowledge")]
    [Authorize(Policy = Permission.MonitoringManage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult AcknowledgeAlert(string id)
    {
        string username = User.Identity?.Name ?? "";
        if (!_alertStore.Acknowledge(id, username))
            return NotFound();
        return NoContent();
    }

    /// <summary>Acknowledge all matching alerts.</summary>
    [HttpPost("alerts/acknowledge-all")]
    [Authorize(Policy = Permission.MonitoringManage)]
    [ProducesResponseType(typeof(object), 200)]
    public IActionResult AcknowledgeAll([FromQuery] AlertSeverity? severity = null)
    {
        string username = User.Identity?.Name ?? "";
        int count = _alertStore.AcknowledgeAll(username, severity);
        return Ok(new { acknowledged = count });
    }

    /// <summary>Get current host metrics (CPU, memory, uptime).</summary>
    [HttpGet("metrics/host")]
    [ProducesResponseType(typeof(HostMetrics), 200)]
    public IActionResult GetHostMetrics()
    {
        return Ok(_metricsCache.GetHostMetrics());
    }

    /// <summary>Get per-VM metrics (CPU, memory, disk I/O, network).</summary>
    [HttpGet("metrics/vms")]
    [ProducesResponseType(typeof(List<VmMetrics>), 200)]
    public IActionResult GetVmMetrics()
    {
        return Ok(_metricsCache.GetVmMetrics());
    }

    /// <summary>Get metrics for a single VM.</summary>
    [HttpGet("metrics/vms/{name}")]
    [ProducesResponseType(typeof(VmMetrics), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetVmMetrics(string name)
    {
        VmMetrics? vm = _metricsCache.GetVmMetrics(name);
        if (vm == null)
            return NotFound();
        return Ok(vm);
    }

    /// <summary>Get storage pool metrics.</summary>
    [HttpGet("metrics/storage")]
    [ProducesResponseType(typeof(List<StorageMetrics>), 200)]
    public IActionResult GetStorageMetrics()
    {
        return Ok(_metricsCache.GetStorageMetrics());
    }

    /// <summary>Get SMART disk health information.</summary>
    [HttpGet("metrics/disks")]
    [Authorize(Policy = Permission.MonitoringManage)]
    [ProducesResponseType(typeof(List<DiskHealthInfo>), 200)]
    public IActionResult GetDiskHealth()
    {
        return Ok(_metricsCache.GetDiskHealth());
    }

    /// <summary>Get monitoring system status (check names, last run times, alert counts).</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(object), 200)]
    public IActionResult GetStatus()
    {
        MonitoringSettings? settings = _settingsService.Load().Monitoring;
        return Ok(
            new
            {
                enabled = settings?.Enabled ?? false,
                lastRunTimes = _monitoringService.LastRunTimes,
                activeAlerts = _alertStore.GetActiveAlertCounts(),
            }
        );
    }

    /// <summary>Get monitoring settings.</summary>
    [HttpGet("settings")]
    [Authorize(Policy = Permission.MonitoringManage)]
    [ProducesResponseType(typeof(MonitoringSettings), 200)]
    public IActionResult GetSettings()
    {
        AppSettings settings = _settingsService.Load();
        return Ok(settings.Monitoring ?? new MonitoringSettings());
    }

    /// <summary>Update monitoring settings.</summary>
    [HttpPut("settings")]
    [Authorize(Policy = Permission.MonitoringManage)]
    [ProducesResponseType(204)]
    public IActionResult UpdateSettings([FromBody] MonitoringSettings monitoring)
    {
        AppSettings settings = _settingsService.Load();
        settings.Monitoring = monitoring;
        _settingsService.Save(settings);
        return NoContent();
    }
}
