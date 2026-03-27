using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Oci;

/// <summary>
/// Pushes snapshots to an OCI-compatible container registry.
/// </summary>
public class OciPushAdapter : ISnapshotPushAdapter
{
    private readonly IVmBackend _vmBackend;
    private readonly TarCompressor _tarCompressor;
    private readonly ITempTracker _tempTracker;
    private readonly ILogger<OciPushAdapter> _logger;
    private static readonly HttpClient Http = CatalogHttpClientFactory.CreateUploadClient();

    public FeedType SupportedType => FeedType.OCI;

    public OciPushAdapter(
        IVmBackend vmBackend,
        TarCompressor tarCompressor,
        ITempTracker tempTracker,
        ILogger<OciPushAdapter> logger
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
            "Pushing snapshot {SnapshotName} of {VmName} to OCI {FeedUrl}",
            snapshotName,
            vmName,
            feed.Url
        );
        string baseUrl = feed.Url.TrimEnd('/');
        string repo = (feed.Repository ?? "").Trim('/');
        AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);
        string tag = BuildTag(origin, snapshotName);

        string tempDir = _tempTracker.CreateTrackedTempDir("vmm_push");

        try
        {
            progress?.Report(new PushProgress("Exporting snapshot...", 5, null));
            await _vmBackend.ExportSnapshotAsync(snapshotId, tempDir);

            progress?.Report(new PushProgress("Compressing...", 20, null));
            string tarPath = await _tarCompressor.CompressAsync(tempDir, progress, ct);

            FileInfo tarInfo = new FileInfo(tarPath);
            long totalBytes = tarInfo.Length;

            progress?.Report(new PushProgress("Computing checksum...", 35, null));
            string digest;
            using (FileStream hashStream = File.OpenRead(tarPath))
            {
                byte[] hash = await SHA256.HashDataAsync(hashStream, ct);
                digest = $"sha256:{Convert.ToHexStringLower(hash)}";
            }

            progress?.Report(new PushProgress("Uploading snapshot...", 40, null));
            await UploadOciBlobStreamedAsync(
                baseUrl,
                repo,
                auth,
                tarPath,
                totalBytes,
                digest,
                progress
            );

            progress?.Report(new PushProgress("Uploading manifest...", 92, null));
            byte[] configBytes = "{}"u8.ToArray();
            string configDigest =
                $"sha256:{Convert.ToHexStringLower(SHA256.HashData(configBytes))}";
            await UploadOciBlobAsync(baseUrl, repo, auth, configBytes, configDigest);

            Dictionary<string, string> annotations = BuildAnnotations(
                vmName,
                snapshotName,
                tag,
                origin
            );
            string manifest = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 2,
                    mediaType = "application/vnd.oci.image.manifest.v1+json",
                    config = new
                    {
                        mediaType = "application/vnd.oci.image.config.v1+json",
                        digest = configDigest,
                        size = configBytes.Length,
                    },
                    layers = new[]
                    {
                        new
                        {
                            mediaType = "application/x-vagrant-box",
                            digest,
                            size = totalBytes,
                        },
                    },
                    annotations,
                }
            );

            HttpRequestMessage manifestReq = new HttpRequestMessage(
                HttpMethod.Put,
                $"{baseUrl}/v2/{repo}/manifests/{tag}"
            );
            if (auth != null)
                manifestReq.Headers.Authorization = auth;
            manifestReq.Content = new StringContent(
                manifest,
                Encoding.UTF8,
                "application/vnd.oci.image.manifest.v1+json"
            );
            await (await Http.SendAsync(manifestReq, ct)).EnsureSuccessWithContextAsync(
                "OCI manifest push"
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

    private static async Task UploadOciBlobStreamedAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth,
        string filePath,
        long totalBytes,
        string digest,
        IProgress<PushProgress>? progress
    )
    {
        HttpRequestMessage initReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/v2/{repo}/blobs/uploads/"
        );
        if (auth != null)
            initReq.Headers.Authorization = auth;
        HttpResponseMessage initResp = await Http.SendAsync(initReq);
        await initResp.EnsureSuccessWithContextAsync("OCI blob upload init");

        string location =
            initResp.Headers.Location?.ToString()
            ?? throw new InvalidOperationException("No upload location returned by registry");
        if (!location.StartsWith("http"))
            location = $"{baseUrl}{location}";
        string sep = location.Contains('?') ? "&" : "?";

        using FileStream fileStream = File.OpenRead(filePath);

        HttpRequestMessage putReq = new HttpRequestMessage(
            HttpMethod.Put,
            $"{location}{sep}digest={digest}"
        );
        if (auth != null)
            putReq.Headers.Authorization = auth;

        HttpResponseMessage response = await StreamCopyHelper.SendWithUploadProgressAsync(
            Http,
            putReq,
            fileStream,
            totalBytes,
            tp =>
            {
                double percent = 40.0 + Math.Min(50.0, tp.FractionComplete * 50.0);
                TimeSpan? eta = tp.EstimatedTimeRemaining;
                string speedText =
                    $"{tp.BytesTransferred / 1024.0 / 1024.0:F0}/{totalBytes / 1024.0 / 1024.0:F0} MB - {tp.SpeedMbPerSecond:F1} MB/s"
                    + (eta.HasValue ? $" - ~{eta.Value:mm\\:ss}" : "");
                progress?.Report(new PushProgress("Uploading...", percent, speedText));
            },
            CancellationToken.None
        );

        await response.EnsureSuccessWithContextAsync("OCI blob upload");
        progress?.Report(new PushProgress("Uploading...", 92, null));
    }

    private static async Task UploadOciBlobAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth,
        byte[] data,
        string digest
    )
    {
        HttpRequestMessage initReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/v2/{repo}/blobs/uploads/"
        );
        if (auth != null)
            initReq.Headers.Authorization = auth;
        HttpResponseMessage initResp = await Http.SendAsync(initReq);
        await initResp.EnsureSuccessWithContextAsync("OCI config upload init");

        string location =
            initResp.Headers.Location?.ToString()
            ?? throw new InvalidOperationException("No upload location returned by registry");
        if (!location.StartsWith("http"))
            location = $"{baseUrl}{location}";
        string sep = location.Contains('?') ? "&" : "?";

        HttpRequestMessage putReq = new HttpRequestMessage(
            HttpMethod.Put,
            $"{location}{sep}digest={digest}"
        );
        if (auth != null)
            putReq.Headers.Authorization = auth;
        putReq.Content = new ByteArrayContent(data);
        putReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        await (await Http.SendAsync(putReq)).EnsureSuccessWithContextAsync("OCI config upload");
    }

    private static Dictionary<string, string> BuildAnnotations(
        string vmName,
        string snapshotName,
        string tag,
        VmOrigin? origin
    )
    {
        Dictionary<string, string> annotations = new Dictionary<string, string>
        {
            [OciAnnotationKeys.Title] = vmName,
            [OciAnnotationKeys.Description] = $"Snapshot \"{snapshotName}\" of {vmName}",
            [OciAnnotationKeys.Created] = DateTime.UtcNow.ToString("o"),
            [OciAnnotationKeys.Version] = tag,
            [OciAnnotationKeys.Snapshot] = "true",
            [OciAnnotationKeys.SnapshotName] = snapshotName,
            [OciAnnotationKeys.PushedBy] = Environment.UserName,
        };

        if (origin != null)
        {
            annotations[OciAnnotationKeys.ParentImageId] = origin.ImageId;
            annotations[OciAnnotationKeys.ParentImageName] = origin.ImageName;
            annotations[OciAnnotationKeys.ParentVersion] = origin.Version;
        }

        return annotations;
    }

    private static string BuildTag(VmOrigin? origin, string snapshotName)
    {
        List<string> parts = new List<string> { "snapshot" };
        if (origin != null && !string.IsNullOrEmpty(origin.ImageId))
            parts.Add(origin.ImageId.Replace(":", "-").Replace("/", "-"));
        parts.Add(Environment.UserName.ToLowerInvariant());
        parts.Add(DateTime.Now.ToString("yyyyMMdd-HHmm"));

        string tag = string.Join("-", parts);
        return Regex.Replace(tag.ToLowerInvariant(), @"[^a-z0-9._-]", "-");
    }
}
