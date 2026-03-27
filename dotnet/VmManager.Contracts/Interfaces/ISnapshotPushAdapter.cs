using VmManager.Contracts.Models;

namespace VmManager.Contracts.Interfaces;

public interface ISnapshotPushAdapter
{
    FeedType SupportedType { get; }

    Task PushAsync(
        FeedConfiguration feed,
        string vmName,
        string snapshotName,
        string snapshotId,
        VmOrigin? origin,
        IProgress<PushProgress>? progress = null,
        CancellationToken ct = default
    );
}
