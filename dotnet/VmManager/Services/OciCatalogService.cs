using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VmManager.Models;

namespace VmManager.Services;

/// <summary>
/// Reads VM image catalogs from an OCI-compliant registry (e.g. Zot) using the
/// OCI Distribution HTTP API. Box files are pushed via ORAS as OCI artifacts.
///
/// Expected layout:
///   registry/repo:tag  →  each tag is a version, the manifest contains a .box blob
///
/// The service translates OCI tags/manifests into the app's VmImage model.
/// </summary>
public class OciCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Lists all available images from the OCI registry by enumerating repositories
    /// and their tags, then fetching manifests to get blob sizes.
    /// </summary>
    public async Task<List<VmImage>> LoadCatalogAsync(AppSettings settings)
    {
        var baseUrl = settings.RegistryUrl.TrimEnd('/');
        var repo = settings.RegistryRepository.Trim('/');
        var auth = BuildAuthHeader(settings);

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(repo))
            throw new InvalidOperationException(
                "OCI Registry URL and Repository are not configured. Set them in Settings."
            );

        // List tags for the repository
        var tags = await ListTagsAsync(baseUrl, repo, auth);

        if (tags.Count == 0)
            return [];

        // Build a VmImage with one version per tag.
        // Read OCI annotations from the first manifest to populate name/description.
        var repoName = repo.Contains('/') ? repo.Split('/').Last() : repo;
        var image = new VmImage
        {
            Id = repo,
            Name = repoName,
            Description = "",
            Versions = [],
        };

        foreach (var tag in tags.OrderByDescending(t => t))
        {
            var manifest = await GetManifestAsync(baseUrl, repo, tag, auth);
            if (manifest == null)
                continue;

            var boxLayer = FindBoxLayer(manifest);
            if (boxLayer == null)
                continue;

            // Use OCI annotations for rich metadata
            var ann = manifest.Annotations ?? new Dictionary<string, string>();

            // Populate image-level fields from the first manifest that has them
            if (string.IsNullOrEmpty(image.Description))
            {
                ann.TryGetValue("org.opencontainers.image.title", out var title);
                ann.TryGetValue("org.opencontainers.image.description", out var desc);
                if (!string.IsNullOrEmpty(title))
                    image.Name = title;
                if (!string.IsNullOrEmpty(desc))
                    image.Description = desc;

                // Features from custom annotation (comma-separated)
                if (
                    ann.TryGetValue("dev.vmmanager.features", out var features)
                    && !string.IsNullOrEmpty(features)
                )
                    image.Features = features.Split(',', StringSplitOptions.TrimEntries).ToList();
            }

            // Parse created date from annotation
            ann.TryGetValue("org.opencontainers.image.created", out var createdStr);
            var date = DateTime.TryParse(createdStr, out var dt) ? dt : DateTime.Now;

            // Version notes
            ann.TryGetValue("org.opencontainers.image.version", out var verNotes);

            image.Versions.Add(
                new VmImageVersion
                {
                    Version = tag,
                    FileName = $"{repo}:{tag}",
                    SizeGb = boxLayer.Size / 1024.0 / 1024.0 / 1024.0,
                    Date = date,
                    Notes = verNotes ?? "",
                }
            );
        }

        // Fallback description
        if (string.IsNullOrEmpty(image.Description))
            image.Description = $"VM image from {baseUrl}/{repo}";

        return image.Versions.Count > 0 ? [image] : [];
    }

    /// <summary>
    /// Returns the download URL for a specific OCI blob (the .box file).
    /// The caller downloads this URL directly.
    /// </summary>
    public async Task<string> GetBlobDownloadUrlAsync(AppSettings settings, string versionTag)
    {
        var baseUrl = settings.RegistryUrl.TrimEnd('/');
        var repo = settings.RegistryRepository.Trim('/');
        var auth = BuildAuthHeader(settings);

        var manifest = await GetManifestAsync(baseUrl, repo, versionTag, auth);
        var boxLayer =
            FindBoxLayer(manifest)
            ?? throw new InvalidOperationException(
                $"No downloadable layer found in manifest for {repo}:{versionTag}"
            );

        return $"{baseUrl}/v2/{repo}/blobs/{boxLayer.Digest}";
    }

    /// <summary>Returns the auth header value for OCI requests, or null if no credentials.</summary>
    public static AuthenticationHeaderValue? BuildAuthHeader(AppSettings settings)
    {
        if (
            string.IsNullOrWhiteSpace(settings.RegistryUsername)
            || string.IsNullOrWhiteSpace(settings.RegistryPassword)
        )
            return null;

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.RegistryUsername}:{settings.RegistryPassword}")
        );
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    /// <summary>
    /// Lists all repositories from a registry using /v2/_catalog.
    /// </summary>
    public static async Task<List<string>> ListRepositoriesAsync(string registryUrl)
    {
        var baseUrl = registryUrl.TrimEnd('/');
        var url = $"{baseUrl}/v2/_catalog";
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OciCatalog>(json, JsonOptions);
        return result?.Repositories ?? [];
    }

    // ── OCI Distribution API calls ──────────────────────────────────────

    private static async Task<List<string>> ListTagsAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth
    )
    {
        var url = $"{baseUrl}/v2/{repo}/tags/list";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth != null)
            request.Headers.Authorization = auth;

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<OciTagList>(json, JsonOptions);
        return result?.Tags ?? [];
    }

    private static async Task<OciManifest?> GetManifestAsync(
        string baseUrl,
        string repo,
        string tag,
        AuthenticationHeaderValue? auth
    )
    {
        var url = $"{baseUrl}/v2/{repo}/manifests/{tag}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
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

        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OciManifest>(json, JsonOptions);
    }

    private static OciLayer? FindBoxLayer(OciManifest? manifest)
    {
        if (manifest?.Layers == null || manifest.Layers.Count == 0)
            return null;

        // Prefer layer with vagrant box media type
        var boxLayer = manifest.Layers.FirstOrDefault(l =>
            l.MediaType?.Contains("vagrant", StringComparison.OrdinalIgnoreCase) == true
        );

        // Fall back to the largest layer (the .box file will dominate)
        return boxLayer ?? manifest.Layers.OrderByDescending(l => l.Size).First();
    }

    // ── OCI JSON models ─────────────────────────────────────────────────

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
