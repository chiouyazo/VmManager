using VmManager.Contracts.Interfaces;

namespace VmManager.Backends.Kvm;

public class KvmPreflightService : IPreflightService
{
    public Task<string?> CheckRamForVmAsync(string vmName)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> CheckDiskSpaceAsync(string targetPath, double requiredGb)
    {
        long availableBytes = GetAvailableDiskSpaceBytes(targetPath);
        long safeRequiredBytes = (long)(requiredGb * 1024 * 1024 * 1024 * 2);
        if (availableBytes < safeRequiredBytes)
        {
            double availGb = availableBytes / (1024.0 * 1024.0 * 1024.0);
            string error =
                $"Not enough disk space at {targetPath}. "
                + $"Required: ~{requiredGb * 2:F1} GB (archive + extraction), "
                + $"Available: {availGb:F1} GB.";
            return Task.FromResult<string?>(error);
        }
        return Task.FromResult<string?>(null);
    }

    public Task<string?> CheckPoolResourcesAsync(int memoryMb, int cpuCount)
    {
        return Task.FromResult<string?>(null);
    }

    private static long GetAvailableDiskSpaceBytes(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return 0L;
        DriveInfo drive = new DriveInfo(root);
        return drive.IsReady ? drive.AvailableFreeSpace : 0L;
    }
}
