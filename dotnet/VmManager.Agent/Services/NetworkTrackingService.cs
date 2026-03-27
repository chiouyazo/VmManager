using System.Text.Json;

namespace VmManager.Agent.Services;

public class NetworkTrackingService
{
    private readonly IAppPaths _paths;
    private readonly ILogger<NetworkTrackingService> _logger;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public NetworkTrackingService(IAppPaths paths, ILogger<NetworkTrackingService> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _paths = paths;
        _logger = logger;
    }

    public List<ManagedNetwork> LoadAll()
    {
        _lock.Wait();
        try
        {
            return LoadInternal();
        }
        finally
        {
            _lock.Release();
        }
    }

    public ManagedNetwork? GetByNetworkId(string networkId)
    {
        _lock.Wait();
        try
        {
            List<ManagedNetwork> networks = LoadInternal();
            return networks.FirstOrDefault(n => n.NetworkId == networkId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void AddReference(string networkId, string switchName, string configHash, string vmName)
    {
        _lock.Wait();
        try
        {
            List<ManagedNetwork> networks = LoadInternal();
            ManagedNetwork? existing = networks.FirstOrDefault(n => n.NetworkId == networkId);

            if (existing != null)
            {
                if (!existing.VmNames.Contains(vmName))
                    existing.VmNames.Add(vmName);
                existing.ReferenceCount = existing.VmNames.Count;
                existing.ConfigHash = configHash;
                existing.LastUsedAt = DateTime.UtcNow;
            }
            else
            {
                networks.Add(
                    new ManagedNetwork
                    {
                        NetworkId = networkId,
                        SwitchName = switchName,
                        ConfigHash = configHash,
                        ReferenceCount = 1,
                        VmNames = [vmName],
                        CreatedAt = DateTime.UtcNow,
                        LastUsedAt = DateTime.UtcNow,
                    }
                );
            }

            Save(networks);
            _logger.LogInformation(
                "Added network reference: {NetworkId} for VM {VmName}",
                networkId,
                vmName
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    public List<string> DecrementReferences(string vmName)
    {
        _lock.Wait();
        try
        {
            List<ManagedNetwork> networks = LoadInternal();
            List<string> zeroed = [];

            foreach (ManagedNetwork network in networks)
            {
                if (!network.VmNames.Remove(vmName))
                    continue;

                network.ReferenceCount = network.VmNames.Count;
                if (network.ReferenceCount == 0)
                    zeroed.Add(network.NetworkId);
            }

            if (zeroed.Count > 0 || networks.Any(n => n.VmNames.Count != n.ReferenceCount))
                Save(networks);

            return zeroed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Remove(string networkId)
    {
        _lock.Wait();
        try
        {
            List<ManagedNetwork> networks = LoadInternal();
            int removed = networks.RemoveAll(n => n.NetworkId == networkId);
            if (removed > 0)
            {
                Save(networks);
                _logger.LogInformation("Removed tracked network {NetworkId}", networkId);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Reconcile(HashSet<string> actualSwitchNames, HashSet<string> actualVmNames)
    {
        _lock.Wait();
        try
        {
            List<ManagedNetwork> networks = LoadInternal();
            bool changed = false;

            for (int i = networks.Count - 1; i >= 0; i--)
            {
                ManagedNetwork network = networks[i];

                if (!actualSwitchNames.Contains(network.SwitchName))
                {
                    _logger.LogInformation(
                        "Reconcile: removing tracked network {NetworkId} -> switch {SwitchName} no longer exists",
                        network.NetworkId,
                        network.SwitchName
                    );
                    networks.RemoveAt(i);
                    changed = true;
                    continue;
                }

                int before = network.VmNames.Count;
                network.VmNames.RemoveAll(vm => !actualVmNames.Contains(vm));
                if (network.VmNames.Count != before)
                {
                    network.ReferenceCount = network.VmNames.Count;
                    changed = true;
                }
            }

            if (changed)
                Save(networks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reconcile network tracking");
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<ManagedNetwork> LoadInternal()
    {
        try
        {
            if (!File.Exists(_paths.ManagedNetworksPath))
                return [];

            string json = File.ReadAllText(_paths.ManagedNetworksPath);
            return JsonSerializer.Deserialize<List<ManagedNetwork>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load managed networks from {Path}",
                _paths.ManagedNetworksPath
            );
            return [];
        }
    }

    private void Save(List<ManagedNetwork> networks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ManagedNetworksPath)!);
        File.WriteAllText(
            _paths.ManagedNetworksPath,
            JsonSerializer.Serialize(networks, WriteOptions)
        );
    }
}
