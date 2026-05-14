using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;

namespace VmManager.Backends.Proxmox;

public class ProxmoxPreflightService : IPreflightService
{
    private readonly ProxmoxApiClient _api;
    private readonly ILogger<ProxmoxPreflightService> _logger;

    public ProxmoxPreflightService(ProxmoxApiClient api, ILogger<ProxmoxPreflightService> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _logger = logger;
    }

    public async Task<string?> CheckRamForVmAsync(string vmName)
    {
        try
        {
            PoolUsage usage = await GetPoolUsageAsync();

            long vmMemMb = 0;
            foreach (PoolMember member in usage.Members)
            {
                if (member.Name.Equals(vmName, StringComparison.OrdinalIgnoreCase))
                    vmMemMb = member.MemoryMb;
            }

            long freeNodeRamMb = await GetNodeFreeRamMbAsync();
            if (vmMemMb > 0 && freeNodeRamMb > 0 && vmMemMb > freeNodeRamMb)
            {
                return $"Not enough RAM on Proxmox node to start {vmName}. "
                    + $"Required: {vmMemMb} MB, Free on node: {freeNodeRamMb} MB.";
            }

            if (_api.MaxPoolMemoryMb > 0)
            {
                long wouldUseMb = usage.TotalRunningMemoryMb + vmMemMb;
                if (wouldUseMb > _api.MaxPoolMemoryMb)
                {
                    return $"Starting {vmName} would exceed pool memory limit. "
                        + $"Pool usage after start: {wouldUseMb} MB, Limit: {_api.MaxPoolMemoryMb} MB. "
                        + "Stop other VMs or increase MaxPoolMemoryMb.";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proxmox RAM preflight check failed for {VmName}", vmName);
        }
        return null;
    }

    public async Task<string?> CheckDiskSpaceAsync(string targetPath, double requiredGb)
    {
        try
        {
            JsonElement storage = await _api.GetAsync<JsonElement>(
                $"/api2/json/nodes/{_api.Node}/storage/{_api.StorageId}/status"
            );
            long availBytes = storage.TryGetProperty("avail", out JsonElement avail)
                ? avail.GetInt64()
                : 0;
            double availGb = availBytes / (1024.0 * 1024.0 * 1024.0);
            double neededGb = requiredGb * 2;

            if (availGb < neededGb)
            {
                return $"Not enough space on Proxmox storage '{_api.StorageId}'. "
                    + $"Required: ~{neededGb:F1} GB, Available: {availGb:F1} GB.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proxmox storage preflight check failed");
        }
        return null;
    }

    public async Task<string?> CheckPoolResourcesAsync(int memoryMb, int cpuCount)
    {
        try
        {
            PoolUsage usage = await GetPoolUsageAsync();

            if (_api.MaxPoolMemoryMb > 0 && usage.TotalMemoryMb + memoryMb > _api.MaxPoolMemoryMb)
            {
                return $"Creating this VM would exceed pool memory limit. "
                    + $"Current pool: {usage.TotalMemoryMb} MB + new VM: {memoryMb} MB = {usage.TotalMemoryMb + memoryMb} MB, "
                    + $"Limit: {_api.MaxPoolMemoryMb} MB.";
            }

            if (_api.MaxPoolCpuCores > 0 && usage.TotalCpuCores + cpuCount > _api.MaxPoolCpuCores)
            {
                return $"Creating this VM would exceed pool CPU limit. "
                    + $"Current pool: {usage.TotalCpuCores} cores + new VM: {cpuCount} cores = {usage.TotalCpuCores + cpuCount} cores, "
                    + $"Limit: {_api.MaxPoolCpuCores} cores.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proxmox pool resource check failed");
        }
        return null;
    }

    private async Task<PoolUsage> GetPoolUsageAsync()
    {
        JsonElement pool = await _api.GetAsync<JsonElement>($"/api2/json/pools/{_api.PoolId}");

        List<PoolMember> members = new List<PoolMember>();
        long totalMemoryMb = 0;
        long totalRunningMemoryMb = 0;
        int totalCpuCores = 0;

        if (pool.TryGetProperty("members", out JsonElement membersEl))
        {
            foreach (JsonElement m in membersEl.EnumerateArray())
            {
                if (m.TryGetProperty("type", out JsonElement type) && type.GetString() != "qemu")
                    continue;

                string status = m.TryGetProperty("status", out JsonElement s)
                    ? s.GetString() ?? ""
                    : "";
                long maxmem = m.TryGetProperty("maxmem", out JsonElement mm) ? mm.GetInt64() : 0;
                int maxcpu = m.TryGetProperty("maxcpu", out JsonElement mc) ? mc.GetInt32() : 0;
                string name = m.TryGetProperty("name", out JsonElement n)
                    ? n.GetString() ?? ""
                    : "";
                long memMb = maxmem / 1024 / 1024;

                members.Add(new PoolMember(name, memMb, maxcpu, status));
                totalMemoryMb += memMb;
                totalCpuCores += maxcpu;

                if (status == "running")
                    totalRunningMemoryMb += memMb;
            }
        }

        return new PoolUsage(members, totalMemoryMb, totalRunningMemoryMb, totalCpuCores);
    }

    private async Task<long> GetNodeFreeRamMbAsync()
    {
        try
        {
            JsonElement nodeStatus = await _api.GetAsync<JsonElement>(
                $"/api2/json/nodes/{_api.Node}/status"
            );
            if (
                nodeStatus.TryGetProperty("memory", out JsonElement mem)
                && mem.TryGetProperty("free", out JsonElement free)
            )
            {
                return free.GetInt64() / 1024 / 1024;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query node free RAM");
        }
        return 0;
    }

    private record PoolMember(string Name, long MemoryMb, int CpuCores, string Status);

    private record PoolUsage(
        List<PoolMember> Members,
        long TotalMemoryMb,
        long TotalRunningMemoryMb,
        int TotalCpuCores
    );
}
