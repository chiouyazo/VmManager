using Microsoft.AspNetCore.Mvc;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly IEnumerable<ICatalogAdapter> _catalogAdapters;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        SettingsService settingsService,
        IEnumerable<ICatalogAdapter> catalogAdapters,
        ILogger<SettingsController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(catalogAdapters);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsService = settingsService;
        _catalogAdapters = catalogAdapters;
        _logger = logger;
    }

    /// <summary>Get current application settings including feeds, VM defaults, and locale configuration.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AppSettings), 200)]
    public IActionResult GetSettings()
    {
        AppSettings settings = _settingsService.Load();
        return Ok(settings);
    }

    /// <summary>Save application settings.</summary>
    [HttpPut]
    [ProducesResponseType(204)]
    public IActionResult SaveSettings([FromBody] AppSettings settings)
    {
        _logger.LogInformation("Saving settings");
        _settingsService.Save(settings);
        return NoContent();
    }

    /// <summary>Test connectivity to a feed. Returns whether the connection succeeded.</summary>
    [HttpPost("feeds/test")]
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

    /// <summary>Discover available repositories on a feed.</summary>
    [HttpPost("feeds/discover")]
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
}
