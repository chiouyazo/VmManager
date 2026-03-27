using VmManager.Contracts.Models;

namespace VmManager.Contracts.Interfaces;

public interface INetworkService
{
    Task<List<SwitchInfo>> GetSwitchesAsync();
    Task CreateSwitchAsync(string switchName, NetworkDefinition def);
    Task UpdateSwitchAsync(string switchName, NetworkDefinition def);
    Task RemoveSwitchAsync(string switchName);
    Task ConfigureVmAdaptersAsync(
        string vmName,
        List<(string SwitchName, VmNetworkAdapter Config)> adapters
    );
    Task ConfigureGuestIpAsync(
        string vmName,
        string username,
        string password,
        List<VmNetworkAdapter> adapters
    );
}
