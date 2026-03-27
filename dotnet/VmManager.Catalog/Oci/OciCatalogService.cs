using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Oci;

/// <summary>
/// Reads VM image catalogs from an OCI-compliant registry (e.g. Zot) using the
/// OCI Distribution HTTP API. Box files are pushed via ORAS as OCI artifacts.
///
/// Expected layout:
///   registry/repo:tag  ->  each tag is a version, the manifest contains a .box blob
///
/// The service translates OCI tags/manifests into the app's VmImage model.
/// </summary>
public class OciCatalogService
{
    private readonly ILogger<OciCatalogService> _logger;

    public OciCatalogService(ILogger<OciCatalogService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient Http = CatalogHttpClientFactory.CreateCatalogClient();

    /// <summary>
    /// Lists all available images from the OCI registry by enumerating repositories
    /// and their tags, then fetching manifests to get blob sizes.
    /// </summary>
    public async Task<List<VmImage>> LoadCatalogAsync(FeedConfiguration feed)
    {
        string baseUrl = feed.Url.TrimEnd('/');
        string repo = (feed.Repository ?? "").Trim('/');
        AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(repo))
            throw new InvalidOperationException(
                "OCI Registry URL and Repository are not configured. Set them in Settings."
            );

        List<string> tags = await ListTagsAsync(baseUrl, repo, auth);

        if (tags.Count == 0)
            return [];

        string repoName = repo.Contains('/') ? repo.Split('/').Last() : repo;
        VmImage image = new VmImage
        {
            Id = repo,
            Name = repoName,
            Description = "",
            Versions = [],
            SourceType = "OCI",
        };

        Task<(string Tag, OciManifest? Manifest)>[] manifestTasks = tags.OrderByDescending(t => t)
            .Select(async tag =>
            {
                OciManifest? manifest = await GetManifestAsync(baseUrl, repo, tag, auth);
                return (tag, manifest);
            })
            .ToArray();

        (string Tag, OciManifest? Manifest)[] results = await Task.WhenAll(manifestTasks);

        foreach ((string tag, OciManifest? manifest) in results)
        {
            if (manifest == null)
                continue;

            OciLayer? boxLayer = FindBoxLayer(manifest);
            if (boxLayer == null)
                continue;

            Dictionary<string, string> ann =
                manifest.Annotations ?? new Dictionary<string, string>();

            if (string.IsNullOrEmpty(image.Description))
            {
                ann.TryGetValue(OciAnnotationKeys.Title, out string? title);
                ann.TryGetValue(OciAnnotationKeys.Description, out string? desc);
                if (!string.IsNullOrEmpty(title))
                    image.Name = title;
                if (!string.IsNullOrEmpty(desc))
                    image.Description = desc;

                if (
                    ann.TryGetValue(OciAnnotationKeys.Features, out string? features)
                    && !string.IsNullOrEmpty(features)
                )
                    image.Features = features.Split(',', StringSplitOptions.TrimEntries).ToList();
            }

            ann.TryGetValue(OciAnnotationKeys.Created, out string? createdStr);
            DateTime date = DateTime.TryParse(createdStr, out DateTime dt) ? dt : DateTime.Now;
            ann.TryGetValue(OciAnnotationKeys.Version, out string? verNotes);
            ann.TryGetValue(OciAnnotationKeys.Snapshot, out string? isSnapshot);
            ann.TryGetValue(OciAnnotationKeys.PushedBy, out string? pushedBy);
            ann.TryGetValue(OciAnnotationKeys.SnapshotName, out string? snapName);
            ann.TryGetValue(OciAnnotationKeys.ParentImageId, out string? parentId);
            ann.TryGetValue(OciAnnotationKeys.ParentImageName, out string? parentName);

            VmImageVersion version = new VmImageVersion
            {
                Version = tag,
                FileName = $"{repo}:{tag}",
                SizeGb = boxLayer.Size / CatalogConstants.BytesPerGb,
                Date = date,
                Notes = verNotes ?? "",
                IsUserSnapshot = isSnapshot == "true",
                PushedBy = pushedBy ?? "",
                ParentImageId = parentId ?? "",
                ParentImageName = parentName ?? "",
            };

            if (version.IsUserSnapshot)
            {
                if (!string.IsNullOrEmpty(snapName))
                    version.Notes = snapName;
                image.UserSnapshots.Add(version);
            }
            else
            {
                image.Versions.Add(version);
            }
        }

        // Fallback
        if (string.IsNullOrEmpty(image.Description))
            image.Description = $"VM image from {baseUrl}/{repo}";

        return (image.Versions.Count > 0 || image.UserSnapshots.Count > 0) ? [image] : [];
    }

    /// <summary>
    /// Returns the download URL for a specific OCI blob (the .box file).
    /// The caller downloads this URL directly.
    /// </summary>
    public async Task<string> GetBlobDownloadUrlAsync(FeedConfiguration feed, string versionTag)
    {
        string baseUrl = feed.Url.TrimEnd('/');
        string repo = (feed.Repository ?? "").Trim('/');
        AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);

        OciManifest? manifest = await GetManifestAsync(baseUrl, repo, versionTag, auth);
        OciLayer boxLayer =
            FindBoxLayer(manifest)
            ?? throw new InvalidOperationException(
                $"No downloadable layer found in manifest for {repo}:{versionTag}"
            );

        return $"{baseUrl}/v2/{repo}/blobs/{boxLayer.Digest}";
    }

    public async Task<bool> TestConnectivityAsync(FeedConfiguration feed)
    {
        try
        {
            string baseUrl = feed.Url.TrimEnd('/');
            using HttpClient client = CatalogHttpClientFactory.CreateTestClient();
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/");
            AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);
            if (auth != null)
                req.Headers.Authorization = auth;
            HttpResponseMessage resp = await client.SendAsync(req);
            return (int)resp.StatusCode < 500;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCI connectivity test failed for {Url}", feed.Url);
            return false;
        }
    }

    /// <summary>
    /// Lists all repositories from a registry using /v2/_catalog.
    /// </summary>
    public async Task<List<string>> ListRepositoriesAsync(FeedConfiguration feed)
    {
        string baseUrl = feed.Url.TrimEnd('/');
        string url = $"{baseUrl}/v2/_catalog";
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);
        if (auth != null)
            request.Headers.Authorization = auth;

        HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        OciCatalog? result = JsonSerializer.Deserialize<OciCatalog>(json, JsonOptions);
        return result?.Repositories ?? [];
    }

    /// <summary>
    /// Lists all repositories from a registry using /v2/_catalog (static convenience overload).
    /// </summary>
    public static async Task<List<string>> ListRepositoriesAsync(string registryUrl)
    {
        string baseUrl = registryUrl.TrimEnd('/');
        string url = $"{baseUrl}/v2/_catalog";
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

        HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        OciCatalog? result = JsonSerializer.Deserialize<OciCatalog>(json, JsonOptions);
        return result?.Repositories ?? [];
    }

    private static async Task<List<string>> ListTagsAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth
    )
    {
        string url = $"{baseUrl}/v2/{repo}/tags/list";
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth != null)
            request.Headers.Authorization = auth;

        HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        OciTagList? result = JsonSerializer.Deserialize<OciTagList>(json, JsonOptions);
        return result?.Tags ?? [];
    }

    private static async Task<OciManifest?> GetManifestAsync(
        string baseUrl,
        string repo,
        string tag,
        AuthenticationHeaderValue? auth
    )
    {
        string url = $"{baseUrl}/v2/{repo}/manifests/{tag}";
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth != null)
            request.Headers.Authorization = auth;

        // Accept both OCI manifest and Docker manifest formats
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json")
        );
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.docker.distribution.manifest.v2+json"
            )
        );

        HttpResponseMessage response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OciManifest>(json, JsonOptions);
    }

    private static OciLayer? FindBoxLayer(OciManifest? manifest)
    {
        if (manifest?.Layers == null || manifest.Layers.Count == 0)
            return null;

        OciLayer? boxLayer = manifest.Layers.FirstOrDefault(l =>
            l.MediaType?.Contains("vagrant", StringComparison.OrdinalIgnoreCase) == true
        );

        // Fall back to the largest layer (the .box file will dominate)
        return boxLayer ?? manifest.Layers.OrderByDescending(l => l.Size).First();
    }

    //  OCI JSON models

    private class OciCatalog
    {
        public List<string> Repositories { get; set; } = [];
    }

    private class OciTagList
    {
        public string? Name { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private class OciManifest
    {
        public int SchemaVersion { get; set; }
        public string? MediaType { get; set; }
        public List<OciLayer> Layers { get; set; } = [];
        public Dictionary<string, string>? Annotations { get; set; }
    }

    private class OciLayer
    {
        public string? MediaType { get; set; }
        public string? Digest { get; set; }
        public long Size { get; set; }
    }
}
