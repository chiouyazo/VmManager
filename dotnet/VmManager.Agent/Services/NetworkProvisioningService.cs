using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VmManager.Agent.Services;

public class NetworkProvisioningService
{
    private readonly INetworkService _networkService;
    private readonly NetworkTrackingService _trackingService;
    private readonly NexusCatalogService _nexusCatalog;
    private readonly CatalogAggregator _catalogAggregator;
    private readonly SettingsService _settingsService;
    private readonly ILogger<NetworkProvisioningService> _logger;

    private readonly Dictionary<string, List<NetworkDefinition>> _networkCache =
        new Dictionary<string, List<NetworkDefinition>>();

    public NetworkProvisioningService(
        INetworkService networkService,
        NetworkTrackingService trackingService,
        NexusCatalogService nexusCatalog,
        CatalogAggregator catalogAggregator,
        SettingsService settingsService,
        ILogger<NetworkProvisioningService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(networkService);
        ArgumentNullException.ThrowIfNull(trackingService);
        ArgumentNullException.ThrowIfNull(nexusCatalog);
        ArgumentNullException.ThrowIfNull(catalogAggregator);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);
        _networkService = networkService;
        _trackingService = trackingService;
        _nexusCatalog = nexusCatalog;
        _catalogAggregator = catalogAggregator;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<List<(string SwitchName, VmNetworkAdapter Config)>> EnsureNetworksAsync(
        List<VmNetworkAdapter> requiredAdapters,
        string vmName,
        string? feedId = null
    )
    {
        List<NetworkDefinition> definitions = await GetNetworkDefinitionsAsync(feedId);
        List<(string SwitchName, VmNetworkAdapter Config)> result = [];

        foreach (VmNetworkAdapter adapter in requiredAdapters)
        {
            NetworkDefinition? def = definitions.FirstOrDefault(d => d.Id == adapter.NetworkId);
            if (def == null)
            {
                _logger.LogWarning(
                    "No network definition found for NetworkId={NetworkId}, skipping",
                    adapter.NetworkId
                );
                continue;
            }

            string switchName = $"VmMgr-{def.Id}";
            string configHash = ComputeConfigHash(def);
            ManagedNetwork? tracked = _trackingService.GetByNetworkId(def.Id);

            if (tracked != null)
            {
                if (tracked.ConfigHash != configHash)
                {
                    _logger.LogInformation(
                        "Config drift detected for {SwitchName}, updating in-place",
                        switchName
                    );
                    await _networkService.UpdateSwitchAsync(switchName, def);
                }
            }
            else
            {
                List<SwitchInfo> existingSwitches = await _networkService.GetSwitchesAsync();
                bool switchExists = existingSwitches.Any(s =>
                    s.Name.Equals(switchName, StringComparison.OrdinalIgnoreCase)
                );

                if (!switchExists)
                {
                    _logger.LogInformation(
                        "Creating switch {SwitchName} for network {NetworkId}",
                        switchName,
                        def.Id
                    );
                    await _networkService.CreateSwitchAsync(switchName, def);
                }
            }

            _trackingService.AddReference(def.Id, switchName, configHash, vmName);
            result.Add((switchName, adapter));
        }

        return result;
    }

    private async Task<List<NetworkDefinition>> GetNetworkDefinitionsAsync(string? feedId)
    {
        if (
            feedId != null
            && _networkCache.TryGetValue(feedId, out List<NetworkDefinition>? cached)
        )
            return cached;

        AppSettings settings = _settingsService.Load();
        List<NetworkDefinition> allDefinitions = [];

        IEnumerable<FeedConfiguration> feeds =
            feedId != null
                ? settings.Feeds.Where(f => f.Id == feedId)
                : settings.Feeds.Where(f => f.Type == FeedType.Nexus);

        foreach (FeedConfiguration feed in feeds)
        {
            try
            {
                List<NetworkDefinition> defs = await _nexusCatalog.LoadNetworksAsync(feed);
                allDefinitions.AddRange(defs);
                _networkCache[feed.Id] = defs;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to load network definitions from feed {FeedId}",
                    feed.Id
                );
            }
        }

        return allDefinitions;
    }

    public static string ComputeConfigHash(NetworkDefinition def)
    {
        string json = JsonSerializer.Serialize(
            def,
            new JsonSerializerOptions { WriteIndented = false }
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
