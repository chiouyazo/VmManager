using System.Management;
using System.Security.Principal;

namespace VmManager.Agent.Services;

public class PreflightService
{
    private readonly ILogger<PreflightService> _logger;

    public PreflightService(ILogger<PreflightService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public static bool IsRunningAsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public Task<bool> IsHyperVAvailableAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\virtualization\v2");
                scope.Connect();
                return scope.IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Hyper-V WMI namespace not accessible");
                return false;
            }
        });
    }

    public Task<long> GetAvailableRamBytesAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"
                );
                foreach (ManagementObject obj in searcher.Get())
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

    public Task<long> GetVmStartupMemoryBytesAsync(string vmName)
    {
        return Task.Run(() =>
        {
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\virtualization\v2");
                scope.Connect();

                SelectQuery query = new SelectQuery(
                    "Msvm_ComputerSystem",
                    $"ElementName='{vmName.Replace("'", "''")}'"
                );
                using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    scope,
                    query
                );
                ManagementObject? vm = searcher
                    .Get()
                    .Cast<ManagementObject>()
                    .FirstOrDefault(o =>
                        (string?)o["Description"] != "Microsoft Hosting Computer System"
                    );
                if (vm == null)
                    return 0L;

                RelatedObjectQuery memQuery = new RelatedObjectQuery(
                    vm.Path.Path,
                    "Msvm_MemorySettingData"
                );
                using ManagementObjectSearcher memSearcher = new ManagementObjectSearcher(
                    scope,
                    memQuery
                );
                ManagementObject? memObj = memSearcher
                    .Get()
                    .Cast<ManagementObject>()
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

    public static Task<long> GetAvailableDiskSpaceBytesAsync(string path)
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
            double availGb = availableBytes / CatalogConstants.BytesPerGb;
            return $"Not enough disk space at {targetPath}. "
                + $"Required: ~{requiredGb * 2:F1} GB (archive + extraction), "
                + $"Available: {availGb:F1} GB.";
        }
        return null;
    }
}
