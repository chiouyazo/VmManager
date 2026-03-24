using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VmManager.Models;

namespace VmManager.Services;

/// <summary>
/// Fetches VM image manifests from a Nexus raw repository.
/// Uses the same pattern as Stork: GET /service/rest/v1/components?repository={name}
/// Images from Nexus are Linux images tagged with "nexus:" prefix.
/// </summary>
public class NexusCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Loads Linux images from a Nexus raw repository.
    /// </summary>
    public async Task<List<VmImage>> LoadCatalogAsync(AppSettings settings)
    {
        var baseUrl = settings.NexusUrl.TrimEnd('/');
        var repo = settings.NexusRepository;
        var auth = BuildAuthHeader(settings);

        var images = new List<VmImage>();
        string? continuationToken = null;

        do
        {
            var url =
                $"{baseUrl}/service/rest/v1/components?repository={Uri.EscapeDataString(repo)}";
            if (continuationToken != null)
                url += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (auth != null)
                request.Headers.Authorization = auth;

            var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<NexusComponentResponse>(json, JsonOptions);

            if (result?.Items != null)
            {
                foreach (var component in result.Items)
                {
                    var manifestAsset = component.Assets?.FirstOrDefault(a =>
                        a.Path?.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
                        == true
                    );

                    if (manifestAsset != null)
                    {
                        var image = await TryParseManifestAsync(
                            baseUrl,
                            auth,
                            manifestAsset,
                            component
                        );
                        if (image != null)
                            images.Add(image);
                    }
                    else
                    {
                        // Treat each downloadable asset as a version
                        var image = BuildImageFromComponent(component);
                        if (image != null)
                            images.Add(image);
                    }
                }
            }

            continuationToken = result?.ContinuationToken;
        } while (continuationToken != null);

        return MergeImages(images);
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
            var manifestUrl =
                manifestAsset.DownloadUrl ?? $"{baseUrl}/repository/{manifestAsset.Path}";
            var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            if (auth != null)
                request.Headers.Authorization = auth;

            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var manifest = JsonSerializer.Deserialize<NexusManifest>(json, JsonOptions);
            if (manifest == null)
                return null;

            var image = new VmImage
            {
                Id = $"nexus:{component.Name}",
                Name = manifest.Title ?? component.Name,
                Description = manifest.Description ?? $"Linux image from Nexus ({component.Name})",
                ImageType = "Linux",
                Features = manifest.Features ?? [],
                Versions = [],
            };

            // Find downloadable assets (non-manifest files)
            if (component.Assets != null)
            {
                foreach (
                    var asset in component.Assets.Where(a =>
                        !a.Path!.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    var fileName = Path.GetFileName(asset.Path ?? "");
                    var sizeGb = (asset.ContentLength ?? 0) / 1024.0 / 1024.0 / 1024.0;

                    image.Versions.Add(
                        new VmImageVersion
                        {
                            Version = manifest.Version ?? ExtractVersion(asset.Path) ?? "latest",
                            FileName = $"nexus:{asset.DownloadUrl ?? asset.Path ?? ""}",
                            SizeGb = sizeGb,
                            Date = asset.LastModified ?? DateTime.Now,
                            Notes = manifest.ReleaseNotes ?? "",
                        }
                    );
                }
            }

            return image.Versions.Count > 0 ? image : null;
        }
        catch
        {
            return null;
        }
    }

    private static VmImage? BuildImageFromComponent(NexusComponent component)
    {
        if (component.Assets == null || component.Assets.Count == 0)
            return null;

        var image = new VmImage
        {
            Id = $"nexus:{component.Name}",
            Name = component.Name,
            Description = $"Linux image from Nexus",
            ImageType = "Linux",
            Versions = [],
        };

        foreach (var asset in component.Assets)
        {
            var sizeGb = (asset.ContentLength ?? 0) / 1024.0 / 1024.0 / 1024.0;
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
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
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
        var merged = new Dictionary<string, VmImage>();
        foreach (var img in images)
        {
            if (merged.TryGetValue(img.Id, out var existing))
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

    /// <summary>Builds a Basic auth header for Nexus requests.</summary>
    public static AuthenticationHeaderValue? BuildAuthHeader(AppSettings settings)
    {
        if (
            string.IsNullOrWhiteSpace(settings.NexusUsername)
            || string.IsNullOrWhiteSpace(settings.NexusPassword)
        )
            return null;

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.NexusUsername}:{settings.NexusPassword}")
        );
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    // ── Nexus JSON models ────────────────────────────────────────────────────

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
        public long? ContentLength { get; set; }
        public DateTime? LastModified { get; set; }
    }

    private class NexusManifest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? ReleaseNotes { get; set; }
        public List<string>? Features { get; set; }
    }
}
