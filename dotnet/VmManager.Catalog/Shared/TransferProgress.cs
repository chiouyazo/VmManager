namespace VmManager.Catalog.Shared;

public readonly record struct TransferProgress(
    long BytesTransferred,
    long TotalBytes,
    TimeSpan Elapsed
)
{
    public double FractionComplete => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes : 0;
    public double SpeedMbPerSecond =>
        BytesTransferred / Math.Max(1.0, Elapsed.TotalSeconds) / 1024.0 / 1024.0;

    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (BytesTransferred <= 0 || TotalBytes <= 0)
                return null;
            double remainingBytes = TotalBytes - BytesTransferred;
            double bytesPerSecond = BytesTransferred / Math.Max(1.0, Elapsed.TotalSeconds);
            return TimeSpan.FromSeconds(remainingBytes / bytesPerSecond);
        }
    }
}
