namespace VmManager.Models;

/// <summary>Rich download progress info for UI display.</summary>
public record DownloadProgress(
    double Percent,
    long DownloadedBytes,
    long TotalBytes,
    double SpeedBytesPerSec,
    TimeSpan Elapsed,
    TimeSpan? Eta
);
