using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Nexus;

public class NexusCatalogService
{
    private readonly ILogger<NexusCatalogService> _logger;

    public NexusCatalogService(ILogger<NexusCatalogService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly HttpClient Http = CatalogHttpClientFactory.CreateCatalogClient();

    /// <summary>
    /// Loads Linux images from a Nexus raw repository.
    /// </summary>
    public async Task<List<VmImage>> LoadCatalogAsync(FeedConfiguration feed)
    {
        _logger.LogDebug(
            "Loading catalog from Nexus {Url} repository {Repo}",
            feed.Url,
            feed.Repository ?? "(all)"
        );
        string baseUrl = feed.Url.TrimEnd('/');
        string repo = feed.Repository ?? "";
        AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);

        List<NexusComponent> allComponents = await LoadComponentsAsync(baseUrl, repo, auth);

        (
            Dictionary<string, NexusAsset> manifestsByDir,
            Dictionary<string, List<NexusAsset>> boxFilesByDir,
            Dictionary<string, NexusAsset> topLevelManifests
        ) = GroupAssetsByPath(allComponents);

        List<VmImage> images = await BuildImagesFromGroupsAsync(
            baseUrl,
            repo,
            auth,
            topLevelManifests,
            boxFilesByDir,
            manifestsByDir
        );

        return MergeImages(images);
    }

    /// <summary>Fetches all Nexus components using paginated API calls.</summary>
    private async Task<List<NexusComponent>> LoadComponentsAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth
    )
    {
        List<NexusComponent> allComponents = new List<NexusComponent>();
        string? continuationToken = null;

        do
        {
            string url =
                $"{baseUrl}/service/rest/v1/components?repository={Uri.EscapeDataString(repo)}";
            if (continuationToken != null)
                url += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            if (auth != null)
                request.Headers.Authorization = auth;

            HttpResponseMessage response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            NexusComponentResponse? result = JsonSerializer.Deserialize<NexusComponentResponse>(
                json,
                JsonOptions
            );

            if (result?.Items != null)
                allComponents.AddRange(result.Items);

            continuationToken = result?.ContinuationToken;
        } while (continuationToken != null);

        return allComponents;
    }

    /// <summary>Groups all assets from components into manifests and box files by directory path.</summary>
    private static (
        Dictionary<string, NexusAsset> ManifestsByDir,
        Dictionary<string, List<NexusAsset>> BoxFilesByDir,
        Dictionary<string, NexusAsset> TopLevelManifests
    ) GroupAssetsByPath(List<NexusComponent> components)
    {
        List<NexusAsset> allAssets = new List<NexusAsset>();
        foreach (NexusComponent comp in components)
        {
            if (comp.Assets != null)
                allAssets.AddRange(comp.Assets);
        }

        Dictionary<string, NexusAsset> manifestsByDir = new Dictionary<string, NexusAsset>();
        Dictionary<string, List<NexusAsset>> boxFilesByDir =
            new Dictionary<string, List<NexusAsset>>();
        Dictionary<string, NexusAsset> topLevelManifests = new Dictionary<string, NexusAsset>();

        foreach (NexusAsset asset in allAssets)
        {
            if (string.IsNullOrEmpty(asset.Path))
                continue;

            string path = asset.Path.TrimStart('/');
            string[] parts = path.Split('/');

            if (
                path.EndsWith(CatalogConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase)
            )
            {
                string dir = string.Join("/", parts.Take(parts.Length - 1));
                if (parts.Length == 2)
                    topLevelManifests[parts[0]] = asset;
                else
                    manifestsByDir[dir] = asset;
            }
            else if (
                path.EndsWith(CatalogConstants.BoxFileExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(
                    CatalogConstants.TarGzExtension,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string dir = string.Join("/", parts.Take(parts.Length - 1));
                if (!boxFilesByDir.ContainsKey(dir))
                    boxFilesByDir[dir] = new List<NexusAsset>();
                boxFilesByDir[dir].Add(asset);
            }
        }

        return (manifestsByDir, boxFilesByDir, topLevelManifests);
    }

    /// <summary>Fetches manifests and builds VmImage objects from grouped assets.</summary>
    private async Task<List<VmImage>> BuildImagesFromGroupsAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth,
        Dictionary<string, NexusAsset> topLevelManifests,
        Dictionary<string, List<NexusAsset>> boxFilesByDir,
        Dictionary<string, NexusAsset> manifestsByDir
    )
    {
        List<Task<VmImage?>> manifestTasks = new List<Task<VmImage?>>();

        foreach (KeyValuePair<string, NexusAsset> kvp in topLevelManifests)
        {
            string imageId = kvp.Key;
            NexusAsset topManifest = kvp.Value;
            List<NexusAsset> versionBoxFiles = new List<NexusAsset>();

            foreach (KeyValuePair<string, List<NexusAsset>> boxKvp in boxFilesByDir)
            {
                if (boxKvp.Key.StartsWith(imageId + "/", StringComparison.OrdinalIgnoreCase))
                    versionBoxFiles.AddRange(boxKvp.Value);
            }

            manifestTasks.Add(
                BuildImageFromManifestAndAssets(
                    baseUrl,
                    repo,
                    auth,
                    imageId,
                    topManifest,
                    versionBoxFiles,
                    manifestsByDir
                )
            );
        }

        VmImage?[] results = await Task.WhenAll(manifestTasks);
        List<VmImage> images = new List<VmImage>();
        foreach (VmImage? img in results)
        {
            if (img != null)
                images.Add(img);
        }

        return images;
    }

    /// <summary>Fetches a top-level manifest and builds a VmImage with versions from box file assets.</summary>
    private async Task<VmImage?> BuildImageFromManifestAndAssets(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth,
        string imageId,
        NexusAsset topManifestAsset,
        List<NexusAsset> boxFiles,
        Dictionary<string, NexusAsset> versionManifests
    )
    {
        try
        {
            NexusManifest? manifest = await ParseManifestAsync(
                baseUrl,
                repo,
                auth,
                topManifestAsset
            );
            if (manifest == null)
                return null;

            VmImage image = new VmImage
            {
                Id = $"nexus:{imageId}",
                Name = manifest.Title ?? imageId,
                Description = manifest.Description ?? $"Image from Nexus ({imageId})",
                ImageType = manifest.ImageType ?? "Windows",
                Features = manifest.Features ?? new List<string>(),
                Versions = new List<VmImageVersion>(),
                SourceType = "Nexus",
            };

            foreach (NexusAsset boxAsset in boxFiles)
            {
                VmImageVersion ver = await CreateVersionFromAssetAsync(
                    baseUrl,
                    repo,
                    auth,
                    boxAsset,
                    versionManifests
                );

                if (ver.IsUserSnapshot)
                    image.UserSnapshots.Add(ver);
                else
                    image.Versions.Add(ver);
            }

            return (image.Versions.Count > 0 || image.UserSnapshots.Count > 0) ? image : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build image from manifest for {ImageId}", imageId);
            return null;
        }
    }

    /// <summary>Fetches and deserializes a manifest JSON from a Nexus asset.</summary>
    private static async Task<NexusManifest?> ParseManifestAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth,
        NexusAsset manifestAsset
    )
    {
        string topPath = (manifestAsset.Path ?? "").TrimStart('/');
        string manifestUrl = $"{baseUrl}/repository/{repo}/{topPath}";
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
        if (auth != null)
            request.Headers.Authorization = auth;

        HttpResponseMessage response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<NexusManifest>(json, JsonOptions);
    }

    /// <summary>Creates a VmImageVersion from a box file asset, optionally loading its version manifest.</summary>
    private async Task<VmImageVersion> CreateVersionFromAssetAsync(
        string baseUrl,
        string repo,
        AuthenticationHeaderValue? auth,
        NexusAsset boxAsset,
        Dictionary<string, NexusAsset> versionManifests
    )
    {
        string assetPath = boxAsset.Path?.TrimStart('/') ?? "";
        string? version = ExtractVersion(assetPath);
        double sizeGb = (boxAsset.FileSize ?? 0) / CatalogConstants.BytesPerGb;

        string versionDir = string.Join(
            "/",
            assetPath.Split('/').Take(assetPath.Split('/').Length - 1)
        );
        NexusManifest? verManifest = null;
        if (versionManifests.TryGetValue(versionDir, out NexusAsset? verManifestAsset))
        {
            try
            {
                verManifest = await ParseManifestAsync(baseUrl, repo, auth, verManifestAsset);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to fetch version manifest for {VersionDir}",
                    versionDir
                );
            }
        }

        string boxPath = (boxAsset.Path ?? "").TrimStart('/');
        string boxDownloadUrl = $"{baseUrl}/repository/{repo}/{boxPath}";
        VmImageVersion ver = new VmImageVersion
        {
            Version = verManifest?.Version ?? version ?? "latest",
            FileName = $"nexus:{boxDownloadUrl}",
            SizeGb = sizeGb,
            Date = boxAsset.LastModified ?? DateTime.Now,
            Notes = verManifest?.ReleaseNotes ?? "",
        };

        ver.Networks = verManifest?.Networks;

        if (verManifest != null && verManifest.Snapshot)
        {
            ver.IsUserSnapshot = true;
            ver.PushedBy = verManifest.PushedBy ?? "";
            ver.ParentImageId = verManifest.ParentImageId ?? "";
            ver.ParentImageName = verManifest.ParentImageName ?? "";
            ver.Notes = verManifest.Title ?? ver.Notes;
        }

        return ver;
    }

    private async Task<VmImage?> TryParseManifestAsync(
        string baseUrl,
        AuthenticationHeaderValue? auth,
        NexusAsset manifestAsset,
        NexusComponent component
    )
    {
        try
        {
            string manifestUrl =
                manifestAsset.DownloadUrl ?? $"{baseUrl}/repository/{manifestAsset.Path}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            if (auth != null)
                request.Headers.Authorization = auth;

            HttpResponseMessage response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync();
            NexusManifest? manifest = JsonSerializer.Deserialize<NexusManifest>(json, JsonOptions);
            if (manifest == null)
                return null;

            VmImage image = new VmImage
            {
                Id = $"nexus:{component.Name}",
                Name = manifest.Title ?? component.Name,
                Description = manifest.Description ?? $"Linux image from Nexus ({component.Name})",
                ImageType = manifest.ImageType ?? "Linux",
                Features = manifest.Features ?? [],
                Versions = [],
                SourceType = "Nexus",
            };

            // Find downloadable assets (non-manifest files)
            if (component.Assets != null)
            {
                foreach (
                    NexusAsset asset in component.Assets.Where(a =>
                        !a.Path!.EndsWith(
                            CatalogConstants.ManifestFileName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    double sizeGb = (asset.FileSize ?? 0) / CatalogConstants.BytesPerGb;

                    VmImageVersion ver = new VmImageVersion
                    {
                        Version = manifest.Version ?? ExtractVersion(asset.Path) ?? "latest",
                        FileName = $"nexus:{asset.DownloadUrl ?? asset.Path ?? ""}",
                        SizeGb = sizeGb,
                        Date = asset.LastModified ?? DateTime.Now,
                        Notes = manifest.ReleaseNotes ?? "",
                        Networks = manifest.Networks,
                    };

                    if (manifest.Snapshot)
                    {
                        ver.IsUserSnapshot = true;
                        ver.PushedBy = manifest.PushedBy ?? "";
                        ver.ParentImageId = manifest.ParentImageId ?? "";
                        ver.ParentImageName = manifest.ParentImageName ?? "";
                        ver.Notes = manifest.Title ?? ver.Notes;
                        image.UserSnapshots.Add(ver);
                    }
                    else
                    {
                        image.Versions.Add(ver);
                    }
                }
            }

            return (image.Versions.Count > 0 || image.UserSnapshots.Count > 0) ? image : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse manifest for component {ComponentName}",
                component.Name
            );
            return null;
        }
    }

    private static VmImage? BuildImageFromComponent(NexusComponent component)
    {
        if (component.Assets == null || component.Assets.Count == 0)
            return null;

        VmImage image = new VmImage
        {
            Id = $"nexus:{component.Name}",
            Name = component.Name,
            Description = "Image from Nexus",
            ImageType = "Linux",
            Versions = [],
            SourceType = "Nexus",
        };

        foreach (NexusAsset asset in component.Assets)
        {
            double sizeGb = (asset.FileSize ?? 0) / CatalogConstants.BytesPerGb;
            image.Versions.Add(
                new VmImageVersion
                {
                    Version = ExtractVersion(asset.Path) ?? "latest",
                    FileName = $"nexus:{asset.DownloadUrl ?? asset.Path ?? ""}",
                    SizeGb = sizeGb,
                    Date = asset.LastModified ?? DateTime.Now,
                }
            );
        }

        return image.Versions.Count > 0 ? image : null;
    }

    private static string? ExtractVersion(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // Try to find a version pattern like "versions/1.2.3/"
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (
                parts[i].Equals("versions", StringComparison.OrdinalIgnoreCase)
                && i + 1 < parts.Length
            )
                return parts[i + 1];
        }

        return null;
    }

    /// <summary>Merges images with the same ID into a single image with combined versions.</summary>
    private static List<VmImage> MergeImages(List<VmImage> images)
    {
        Dictionary<string, VmImage> merged = new Dictionary<string, VmImage>();
        foreach (VmImage img in images)
        {
            if (merged.TryGetValue(img.Id, out VmImage? existing))
            {
                existing.Versions.AddRange(img.Versions);
            }
            else
            {
                merged[img.Id] = img;
            }
        }

        return [.. merged.Values];
    }

    public async Task<List<NetworkDefinition>> LoadNetworksAsync(FeedConfiguration feed)
    {
        try
        {
            string baseUrl = feed.Url.TrimEnd('/');
            string repo = feed.Repository ?? "";
            AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);

            string url = $"{baseUrl}/repository/{Uri.EscapeDataString(repo)}/networks.json";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            if (auth != null)
                request.Headers.Authorization = auth;

            using HttpClient client = CatalogHttpClientFactory.CreateTestClient();
            HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return [];

            string json = await response.Content.ReadAsStringAsync();
            NetworksManifest? manifest = JsonSerializer.Deserialize<NetworksManifest>(
                json,
                JsonOptions
            );
            return manifest?.Networks ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No networks.json found for feed {FeedUrl}", feed.Url);
            return [];
        }
    }

    public async Task<bool> TestConnectivityAsync(FeedConfiguration feed)
    {
        try
        {
            string baseUrl = feed.Url.TrimEnd('/');
            string repo = feed.Repository ?? "";
            using HttpClient client = CatalogHttpClientFactory.CreateTestClient();
            string url =
                $"{baseUrl}/service/rest/v1/components?repository={Uri.EscapeDataString(repo)}";
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
            AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(feed);
            if (auth != null)
                req.Headers.Authorization = auth;
            HttpResponseMessage resp = await client.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nexus connectivity test failed for {Url}", feed.Url);
            return false;
        }
    }

    public async Task<List<string>> ListRawRepositoriesAsync(FeedConfiguration feed)
    {
        return await ListRawRepositoriesAsync(feed.Url, feed.Username, feed.Password);
    }

    public static async Task<List<string>> ListRawRepositoriesAsync(
        string nexusUrl,
        string? username,
        string? password
    )
    {
        string url = $"{nexusUrl.TrimEnd('/')}/service/rest/v1/repositories";
        using HttpClient client = CatalogHttpClientFactory.CreateTestClient();
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        AuthenticationHeaderValue? auth = AuthHelper.BuildBasicAuth(username, password);
        if (auth != null)
            request.Headers.Authorization = auth;
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        List<NexusRepoInfo>? repos = JsonSerializer.Deserialize<List<NexusRepoInfo>>(
            json,
            JsonOptions
        );
        return repos
                ?.Where(r => string.Equals(r.Format, "raw", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Name ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList() ?? new List<string>();
    }

    private class NexusRepoInfo
    {
        public string? Name { get; set; }
        public string? Format { get; set; }
    }

    // Nexus JSON models

    private class NexusComponentResponse
    {
        public List<NexusComponent> Items { get; set; } = [];
        public string? ContinuationToken { get; set; }
    }

    private class NexusComponent
    {
        public string Name { get; set; } = "";
        public string? Group { get; set; }
        public string? Version { get; set; }
        public List<NexusAsset>? Assets { get; set; }
    }

    private class NexusAsset
    {
        public string? Path { get; set; }
        public string? DownloadUrl { get; set; }
        public long? FileSize { get; set; }
        public DateTime? LastModified { get; set; }
    }

    private class NexusManifest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? ReleaseNotes { get; set; }
        public List<string>? Features { get; set; }
        public string? ImageType { get; set; }
        public bool Snapshot { get; set; }
        public string? PushedBy { get; set; }
        public string? ParentImageId { get; set; }
        public string? ParentImageName { get; set; }
        public string? ParentVersion { get; set; }
        public string? VmName { get; set; }
        public List<VmNetworkAdapter>? Networks { get; set; }
    }
}
