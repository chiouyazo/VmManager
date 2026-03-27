namespace VmManager.Agent.Services;

public class SnapshotPushService
{
    private readonly Dictionary<FeedType, ISnapshotPushAdapter> _adapters;

    public SnapshotPushService(IEnumerable<ISnapshotPushAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToDictionary(a => a.SupportedType);
    }

    public Task PushAsync(
        FeedConfiguration feed,
        string vmName,
        string snapshotName,
        string snapshotId,
        VmOrigin? origin,
        IProgress<PushProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        if (!_adapters.TryGetValue(feed.Type, out ISnapshotPushAdapter? adapter))
            throw new InvalidOperationException($"No push adapter for feed type {feed.Type}");

        return adapter.PushAsync(feed, vmName, snapshotName, snapshotId, origin, progress, ct);
    }
}
