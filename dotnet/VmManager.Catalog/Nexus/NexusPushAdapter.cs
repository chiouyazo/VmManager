using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Nexus;

/// <summary>
/// Pushes snapshots to a Nexus raw repository via HTTP PUT.
/// </summary>
public class NexusPushAdapter : ISnapshotPushAdapter
{
    private readonly IVmBackend _vmBackend;
    private readonly TarCompressor _tarCompressor;
    private readonly ITempTracker _tempTracker;
    private readonly ILogger<NexusPushAdapter> _logger;
    private static readonly HttpClient Http = CatalogHttpClientFactory.CreateUploadClient();

    public FeedType SupportedType => FeedType.Nexus;

    public NexusPushAdapter(
        IVmBackend vmBackend,
        TarCompressor tarCompressor,
        ITempTracker tempTracker,
        ILogger<NexusPushAdapter> logger
    )
    {
        ArgumentNullException.ThrowIfNull(vmBackend);
        ArgumentNullException.ThrowIfNull(tarCompressor);
        ArgumentNullException.ThrowIfNull(tempTracker);
        ArgumentNullException.ThrowIfNull(logger);
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
        _logger.LogInformation(
            "Pushing snapshot {SnapshotName} of {VmName} to Nexus {FeedUrl}",
            snapshotName,
            vmName,
            feed.Url
        );
        string baseUrl = feed.Url.TrimEnd('/');
        AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);

        string repo = feed.Repository ?? origin?.Repository ?? "";
        if (string.IsNullOrEmpty(repo))
            throw new InvalidOperationException(
                "No Nexus repository specified. The push flow should resolve the repository before calling this method."
            );

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string username = Environment.UserName;
        string imageId = origin?.ImageId ?? vmName;
        VersionReference imageRef = VersionReference.Parse(imageId);
        string cleanImageId = imageRef is VersionReference.Nexus nexusRef
            ? nexusRef.DownloadUrl
            : imageId;
        string snapshotDir =
            $"{cleanImageId}/{CatalogConstants.SnapshotsDirName}/{username}-{timestamp}";

        _logger.LogDebug(
            "Push target: {BaseUrl}/repository/{Repo}/{Dir}",
            baseUrl,
            repo,
            snapshotDir
        );

        progress?.Report(new PushProgress("Checking write access...", 2, null));
        HttpRequestMessage testReq = new HttpRequestMessage(
            HttpMethod.Put,
            $"{baseUrl}/repository/{repo}/{cleanImageId}/{CatalogConstants.SnapshotsDirName}/.writetest"
        );
        if (auth != null)
            testReq.Headers.Authorization = auth;
        testReq.Content = new StringContent("test", Encoding.UTF8);
        try
        {
            HttpResponseMessage testResp = await Http.SendAsync(testReq, ct);
            if (
                testResp.StatusCode == HttpStatusCode.Unauthorized
                || testResp.StatusCode == HttpStatusCode.Forbidden
            )
                throw new InvalidOperationException(
                    $"No write permission to Nexus repository '{repo}'. Check your credentials."
                );

            HttpRequestMessage deleteTest = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{baseUrl}/repository/{repo}/{cleanImageId}/{CatalogConstants.SnapshotsDirName}/.writetest"
            );
            if (auth != null)
                deleteTest.Headers.Authorization = auth;
            await Http.SendAsync(deleteTest, ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch { }

        string tempDir = _tempTracker.CreateTrackedTempDir("vmm_push");

        try
        {
            progress?.Report(new PushProgress("Exporting snapshot...", 5, null));
            await _vmBackend.ExportSnapshotAsync(snapshotId, tempDir);

            progress?.Report(new PushProgress("Compressing...", 20, null));
            string tarPath = await _tarCompressor.CompressAsync(tempDir, progress, ct);

            FileInfo tarInfo = new FileInfo(tarPath);
            progress?.Report(new PushProgress("Uploading snapshot...", 40, null));

            await UploadFileStreamedAsync(
                $"{baseUrl}/repository/{repo}/{snapshotDir}/snapshot{CatalogConstants.BoxFileExtension}",
                auth,
                tarPath,
                tarInfo.Length,
                progress
            );

            progress?.Report(new PushProgress("Uploading manifest...", 95, null));
            string manifestJson = JsonSerializer.Serialize(
                new
                {
                    snapshot = true,
                    title = snapshotName,
                    version = $"{username}-{timestamp}",
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

            HttpRequestMessage manifestReq = new HttpRequestMessage(
                HttpMethod.Put,
                $"{baseUrl}/repository/{repo}/{snapshotDir}/{CatalogConstants.ManifestFileName}"
            );
            if (auth != null)
                manifestReq.Headers.Authorization = auth;
            manifestReq.Content = new StringContent(
                manifestJson,
                Encoding.UTF8,
                "application/json"
            );
            await (await Http.SendAsync(manifestReq, ct)).EnsureSuccessWithContextAsync(
                "Nexus manifest upload"
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

    private static async Task UploadFileStreamedAsync(
        string url,
        AuthenticationHeaderValue? auth,
        string filePath,
        long totalBytes,
        IProgress<PushProgress>? progress
    )
    {
        using FileStream fileStream = File.OpenRead(filePath);

        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Put, url);
        if (auth != null)
            req.Headers.Authorization = auth;

        HttpResponseMessage response = await StreamCopyHelper.SendWithUploadProgressAsync(
            Http,
            req,
            fileStream,
            totalBytes,
            tp =>
            {
                double percent = 40.0 + Math.Min(55.0, tp.FractionComplete * 55.0);
                TimeSpan? eta = tp.EstimatedTimeRemaining;
                string speedText =
                    $"{tp.BytesTransferred / 1024.0 / 1024.0:F0}/{totalBytes / 1024.0 / 1024.0:F0} MB - {tp.SpeedMbPerSecond:F1} MB/s"
                    + (eta.HasValue ? $" - ~{eta.Value:mm\\:ss}" : "");
                progress?.Report(new PushProgress("Uploading...", percent, speedText));
            },
            CancellationToken.None
        );

        await response.EnsureSuccessWithContextAsync("Nexus file upload");
        progress?.Report(new PushProgress("Uploading...", 95, null));
    }
}
