namespace VmManager.Contracts.Models;

public record DownloadProgress(
    double Percent,
    long DownloadedBytes,
    long TotalBytes,
    double SpeedBytesPerSec,
    TimeSpan Elapsed,
    TimeSpan? Eta
);
