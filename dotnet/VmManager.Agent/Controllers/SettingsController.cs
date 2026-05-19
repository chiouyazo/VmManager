using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly IEnumerable<ICatalogAdapter> _catalogAdapters;
    private readonly AuthorizationService _authorizationService;
    private readonly EmailService _emailService;
    private readonly QuotaService _quotaService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        SettingsService settingsService,
        IEnumerable<ICatalogAdapter> catalogAdapters,
        AuthorizationService authorizationService,
        EmailService emailService,
        QuotaService quotaService,
        ILogger<SettingsController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(catalogAdapters);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(emailService);
        ArgumentNullException.ThrowIfNull(quotaService);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsService = settingsService;
        _catalogAdapters = catalogAdapters;
        _authorizationService = authorizationService;
        _emailService = emailService;
        _quotaService = quotaService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = Permission.SettingsView)]
    [ProducesResponseType(typeof(AppSettings), 200)]
    public IActionResult GetSettings()
    {
        AppSettings settings = _settingsService.Load();
        return Ok(settings);
    }

    [HttpPut]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    public IActionResult SaveSettings([FromBody] AppSettings settings)
    {
        AppSettings current = _settingsService.Load();

        bool feedsChanged = !FeedsEqual(current.Feeds, settings.Feeds);
        bool scriptsChanged =
            current.PostCreationScript != settings.PostCreationScript
            || current.PostStartupScript != settings.PostStartupScript;
        bool defaultsChanged =
            current.DefaultMemoryMb != settings.DefaultMemoryMb
            || current.DefaultCpuCount != settings.DefaultCpuCount
            || current.DefaultVmUsername != settings.DefaultVmUsername
            || current.DefaultVmPassword != settings.DefaultVmPassword
            || current.DefaultLocale != settings.DefaultLocale
            || current.DefaultKeyboardLayout != settings.DefaultKeyboardLayout
            || current.DefaultTimezone != settings.DefaultTimezone
            || current.ApplyLocaleOnCreate != settings.ApplyLocaleOnCreate
            || current.RenameComputerToVmName != settings.RenameComputerToVmName
            || current.LocalVmPath != settings.LocalVmPath
            || current.AutoCleanupUnusedNetworks != settings.AutoCleanupUnusedNetworks;

        if (
            feedsChanged
            && !_authorizationService.HasPermission(User, Permission.SettingsManageFeeds)
        )
            return Forbid();
        if (
            scriptsChanged
            && !_authorizationService.HasPermission(User, Permission.SettingsEditScripts)
        )
            return Forbid();
        if (
            defaultsChanged
            && !_authorizationService.HasPermission(User, Permission.SettingsEditVmDefaults)
        )
            return Forbid();

        _logger.LogInformation("Saving settings");
        _settingsService.Save(settings);
        return NoContent();
    }

    [HttpPost("feeds/test")]
    [Authorize(Policy = Permission.SettingsManageFeeds)]
    [ProducesResponseType(typeof(object), 200)]
    public async Task<IActionResult> TestFeedConnection(
        [FromBody] FeedConfiguration feed,
        CancellationToken cancellationToken
    )
    {
        ICatalogAdapter? adapter = _catalogAdapters.FirstOrDefault(a =>
            a.SupportedType == feed.Type
        );
        if (adapter == null)
            return BadRequest(new { error = "Unsupported feed type: " + feed.Type });

        bool success = await adapter.TestConnectivityAsync(feed, cancellationToken);
        return Ok(new { success });
    }

    [HttpPost("feeds/discover")]
    [Authorize(Policy = Permission.SettingsManageFeeds)]
    [ProducesResponseType(typeof(List<string>), 200)]
    public async Task<IActionResult> DiscoverRepositories(
        [FromBody] FeedConfiguration feed,
        CancellationToken cancellationToken
    )
    {
        ICatalogAdapter? adapter = _catalogAdapters.FirstOrDefault(a =>
            a.SupportedType == feed.Type
        );
        if (adapter == null)
            return BadRequest(new { error = "Unsupported feed type: " + feed.Type });

        List<string> repos = await adapter.DiscoverRepositoriesAsync(feed, cancellationToken);
        return Ok(repos);
    }

    [HttpPost("test-email")]
    [Authorize(Policy = Permission.SettingsEditScripts)]
    [ProducesResponseType(typeof(EmailTestResult), 200)]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailRequest request)
    {
        EmailTestResult result = await _emailService.TestAsync(
            request.ToAddress,
            request.SmtpHost,
            request.SmtpPort,
            request.SmtpUsername,
            request.SmtpPassword,
            request.SmtpFromAddress,
            request.SmtpUseTls
        );
        return Ok(result);
    }

    [HttpGet("quota")]
    [ProducesResponseType(typeof(QuotaUsage), 200)]
    public async Task<IActionResult> GetMyQuota()
    {
        string username = User.Identity?.Name ?? "admin";
        QuotaUsage usage = await _quotaService.GetUsageAsync(username);
        return Ok(usage);
    }

    private static bool FeedsEqual(List<FeedConfiguration> a, List<FeedConfiguration> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (
                a[i].Id != b[i].Id
                || a[i].Name != b[i].Name
                || a[i].Url != b[i].Url
                || a[i].Repository != b[i].Repository
                || a[i].Username != b[i].Username
                || a[i].Password != b[i].Password
                || a[i].Type != b[i].Type
            )
                return false;
        }
        return true;
    }
}
