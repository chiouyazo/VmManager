namespace VmManager.Contracts.Interfaces;

public interface IPreflightService
{
    Task<string?> CheckRamForVmAsync(string vmName);
    Task<string?> CheckDiskSpaceAsync(string targetPath, double requiredGb);
    Task<string?> CheckPoolResourcesAsync(int memoryMb, int cpuCount);
}
