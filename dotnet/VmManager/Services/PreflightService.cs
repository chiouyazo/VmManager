using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Principal;

namespace VmManager.Services;

/// <summary>
/// Pre-flight checks via WMI (no PowerShell process spawning).
/// </summary>
public class PreflightService
{
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public Task<bool> IsHyperVAvailableAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\virtualization\v2");
                scope.Connect();
                return scope.IsConnected;
            }
            catch
            {
                return false;
            }
        });
    }

    public Task<bool> IsDockerAvailableAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("docker")
                {
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var process = Process.Start(psi);
                if (process == null)
                    return false;
                using (process)
                {
                    process.WaitForExit(10_000);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
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
                using var searcher = new ManagementObjectSearcher(
                    "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"
                );
                foreach (ManagementObject obj in searcher.Get())
                {
                    var freeKb = (ulong)obj["FreePhysicalMemory"];
                    return (long)(freeKb * 1024);
                }
            }
            catch { }
            return 0L;
        });
    }

    public Task<long> GetVmStartupMemoryBytesAsync(string vmName)
    {
        return Task.Run(() =>
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\virtualization\v2");
                scope.Connect();

                var query = new SelectQuery(
                    "Msvm_ComputerSystem",
                    $"ElementName='{vmName.Replace("'", "''")}' AND Caption='Virtual Machine'"
                );
                using var searcher = new ManagementObjectSearcher(scope, query);
                var vm = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (vm == null)
                    return 0L;

                var memQuery = new RelatedObjectQuery(vm.Path.Path, "Msvm_MemorySettingData");
                using var memSearcher = new ManagementObjectSearcher(scope, memQuery);
                var memObj = memSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (memObj != null)
                {
                    var limit = memObj["Limit"];
                    if (limit != null)
                        return (long)(ulong)limit * 1024 * 1024;
                }
            }
            catch { }
            return 0L;
        });
    }

    public static Task<long> GetAvailableDiskSpaceBytesAsync(string path)
    {
        return Task.Run(() =>
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return 0L;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0L;
        });
    }

    public async Task<string?> CheckRamForVmAsync(string vmName)
    {
        var availableBytes = await GetAvailableRamBytesAsync();
        var requiredBytes = await GetVmStartupMemoryBytesAsync(vmName);
        if (requiredBytes <= 0)
            return null;
        if (availableBytes < requiredBytes)
        {
            var availMb = availableBytes / 1024 / 1024;
            var reqMb = requiredBytes / 1024 / 1024;
            return $"Not enough RAM to start {vmName}. "
                + $"Required: {reqMb} MB, Available: {availMb} MB. "
                + "Close other VMs or applications to free memory.";
        }
        return null;
    }

    public async Task<string?> CheckDiskSpaceAsync(string targetPath, double requiredGb)
    {
        var availableBytes = await GetAvailableDiskSpaceBytesAsync(targetPath);
        var safeRequiredBytes = (long)(requiredGb * 1024 * 1024 * 1024 * 2);
        if (availableBytes < safeRequiredBytes)
        {
            var availGb = availableBytes / 1024.0 / 1024.0 / 1024.0;
            return $"Not enough disk space at {targetPath}. "
                + $"Required: ~{requiredGb * 2:F1} GB (archive + extraction), "
                + $"Available: {availGb:F1} GB.";
        }
        return null;
    }
}
