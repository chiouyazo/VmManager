using VmManager.Catalog.Shared;

namespace VmManager.Agent.Services;

public class ExpiredEnvironmentCleanupService : BackgroundService
{
    private readonly EnvironmentService _environments;
    private readonly SettingsService _settings;
    private readonly ILogger<ExpiredEnvironmentCleanupService> _logger;

    public ExpiredEnvironmentCleanupService(
        EnvironmentService environments,
        SettingsService settings,
        ILogger<ExpiredEnvironmentCleanupService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(environments);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _environments = environments;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            AppSettings settings = _settings.Load();
            int intervalMinutes =
                settings.EnvCleanupIntervalMinutes > 0 ? settings.EnvCleanupIntervalMinutes : 15;

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                int deleted = await _environments.CleanupAsync(settings.EnvExpiryWarnLeadMinutes);
                if (deleted > 0)
                    _logger.LogInformation(
                        "Environment reaper deleted {Count} expired env(s)",
                        deleted
                    );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Environment cleanup pass failed");
            }
        }
    }
}
