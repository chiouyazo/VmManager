using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;

namespace VmManager.Backends.HyperV;

[SupportedOSPlatform("windows")]
public class HyperVPreflightService : IPreflightService
{
    private readonly ILogger<HyperVPreflightService> _logger;

    public HyperVPreflightService(ILogger<HyperVPreflightService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> CheckRamForVmAsync(string vmName)
    {
        long availableBytes = await GetAvailableRamBytesAsync();
        long requiredBytes = await GetVmStartupMemoryBytesAsync(vmName);
        if (requiredBytes <= 0)
            return null;

        if (availableBytes < requiredBytes)
        {
            long availMb = availableBytes / 1024 / 1024;
            long reqMb = requiredBytes / 1024 / 1024;
            return $"Not enough RAM to start {vmName}. "
                + $"Required: {reqMb} MB, Available: {availMb} MB. "
                + "Close other VMs or applications to free memory.";
        }
        return null;
    }

    public async Task<string?> CheckDiskSpaceAsync(string targetPath, double requiredGb)
    {
        long availableBytes = await GetAvailableDiskSpaceBytesAsync(targetPath);
        long safeRequiredBytes = (long)(requiredGb * 1024 * 1024 * 1024 * 2);
        if (availableBytes < safeRequiredBytes)
        {
            double availGb = availableBytes / (1024.0 * 1024.0 * 1024.0);
            return $"Not enough disk space at {targetPath}. "
                + $"Required: ~{requiredGb * 2:F1} GB (archive + extraction), "
                + $"Available: {availGb:F1} GB.";
        }
        return null;
    }

    public Task<string?> CheckPoolResourcesAsync(int memoryMb, int cpuCount)
    {
        return Task.FromResult<string?>(null);
    }

    private Task<long> GetAvailableRamBytesAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                using System.Management.ManagementObjectSearcher searcher =
                    new System.Management.ManagementObjectSearcher(
                        "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"
                    );
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    ulong freeKb = (ulong)obj["FreePhysicalMemory"];
                    return (long)(freeKb * 1024);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query available RAM via WMI");
            }
            return 0L;
        });
    }

    private Task<long> GetVmStartupMemoryBytesAsync(string vmName)
    {
        return Task.Run(() =>
        {
            try
            {
                System.Management.ManagementScope scope = new System.Management.ManagementScope(
                    @"\\.\root\virtualization\v2"
                );
                scope.Connect();

                System.Management.SelectQuery query = new System.Management.SelectQuery(
                    "Msvm_ComputerSystem",
                    $"ElementName='{vmName.Replace("'", "''")}'"
                );
                using System.Management.ManagementObjectSearcher searcher =
                    new System.Management.ManagementObjectSearcher(scope, query);
                System.Management.ManagementObject? vm = searcher
                    .Get()
                    .Cast<System.Management.ManagementObject>()
                    .FirstOrDefault(o =>
                        (string?)o["Description"] != "Microsoft Hosting Computer System"
                    );
                if (vm == null)
                    return 0L;

                System.Management.RelatedObjectQuery memQuery =
                    new System.Management.RelatedObjectQuery(
                        vm.Path.Path,
                        "Msvm_MemorySettingData"
                    );
                using System.Management.ManagementObjectSearcher memSearcher =
                    new System.Management.ManagementObjectSearcher(scope, memQuery);
                System.Management.ManagementObject? memObj = memSearcher
                    .Get()
                    .Cast<System.Management.ManagementObject>()
                    .FirstOrDefault();
                if (memObj != null)
                {
                    object? limit = memObj["Limit"];
                    if (limit != null)
                        return (long)(ulong)limit * 1024 * 1024;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query VM startup memory for {VmName}", vmName);
            }
            return 0L;
        });
    }

    private static Task<long> GetAvailableDiskSpaceBytesAsync(string path)
    {
        return Task.Run(() =>
        {
            string? root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return 0L;
            DriveInfo drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0L;
        });
    }
}
