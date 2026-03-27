using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Local;

/// <summary>
/// Pushes snapshots to a local or network file path.
/// </summary>
public class LocalPushAdapter : ISnapshotPushAdapter
{
    private readonly IVmBackend _vmBackend;
    private readonly TarCompressor _tarCompressor;
    private readonly ITempTracker _tempTracker;
    private readonly ILogger<LocalPushAdapter> _logger;
    public FeedType SupportedType => FeedType.Local;

    public LocalPushAdapter(
        IVmBackend vmBackend,
        TarCompressor tarCompressor,
        ITempTracker tempTracker,
        ILogger<LocalPushAdapter> logger
    )
    {
        _vmBackend = vmBackend;
        _tarCompressor = tarCompressor;
        _tempTracker = tempTracker;
        _logger = logger;
    }

    public async Task PushAsync(
        FeedConfiguration feed,
        string vmName,
        string snapshotName,
        string snapshotId,
        VmOrigin? origin,
        IProgress<PushProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        string localCatalogPath = feed.Url;
        string username = Environment.UserDomainName + "\\" + Environment.UserName;
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string imageId = origin?.ImageId ?? vmName;
        string safePath = imageId.Replace(":", "_").Replace("/", "_").Replace("local_", "");
        string destDir = Path.Combine(
            localCatalogPath,
            CatalogConstants.SnapshotsDirName,
            safePath,
            $"{username}-{timestamp}"
        );
        Directory.CreateDirectory(destDir);

        string tempDir = _tempTracker.CreateTrackedTempDir("vmm_push");

        try
        {
            progress?.Report(new PushProgress("Exporting snapshot...", 5, null));
            await _vmBackend.ExportSnapshotAsync(snapshotId, tempDir);

            progress?.Report(new PushProgress("Compressing...", 20, null));
            string tarPath = await _tarCompressor.CompressAsync(tempDir, progress, ct);

            FileInfo tarInfo = new FileInfo(tarPath);
            string destFile = Path.Combine(destDir, "snapshot" + CatalogConstants.BoxFileExtension);
            long totalBytes = tarInfo.Length;

            progress?.Report(new PushProgress("Copying to network...", 40, null));

            using (FileStream source = File.OpenRead(tarPath))
            using (FileStream dest = File.Create(destFile))
            {
                await StreamCopyHelper.CopyWithProgressAsync(
                    source,
                    dest,
                    totalBytes,
                    tp =>
                    {
                        double percent = 40.0 + (tp.FractionComplete * 55.0);
                        TimeSpan? eta = tp.EstimatedTimeRemaining;
                        string speedText =
                            $"{tp.BytesTransferred / 1024.0 / 1024.0:F0}/{totalBytes / 1024.0 / 1024.0:F0} MB - {tp.SpeedMbPerSecond:F1} MB/s"
                            + (eta.HasValue ? $" - ~{eta.Value:mm\\:ss} remaining" : "");
                        progress?.Report(
                            new PushProgress("Copying to network...", percent, speedText)
                        );
                    },
                    ct
                );
            }

            string manifestJson = JsonSerializer.Serialize(
                new
                {
                    snapshot = true,
                    title = snapshotName,
                    description = $"Snapshot \"{snapshotName}\" of {vmName}",
                    pushedBy = username,
                    pushedAt = DateTime.UtcNow.ToString("o"),
                    parentImageId = origin?.ImageId ?? "",
                    parentImageName = origin?.ImageName ?? "",
                    parentVersion = origin?.Version ?? "",
                    vmName,
                },
                new JsonSerializerOptions { WriteIndented = true }
            );

            await File.WriteAllTextAsync(
                Path.Combine(destDir, CatalogConstants.ManifestFileName),
                manifestJson,
                ct
            );
            progress?.Report(new PushProgress("Done", 100, null));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { }
            _tempTracker.Unregister(tempDir);
        }
    }
}
