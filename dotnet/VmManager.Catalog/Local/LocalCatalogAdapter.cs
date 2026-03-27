using System.Text.Json;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Local;

public class LocalCatalogAdapter : ICatalogAdapter
{
    public FeedType SupportedType => FeedType.Local;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<List<VmImage>> LoadCatalogAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    )
    {
        string catalogPath = feed.Url;
        if (string.IsNullOrWhiteSpace(catalogPath))
            return [];

        string catalogFile = Path.Combine(catalogPath, "catalog.json");

        string? json = await Task.Run(
            () =>
            {
                if (!File.Exists(catalogFile))
                    return null;
                return File.ReadAllText(catalogFile);
            },
            ct
        );

        if (json == null)
            return [];

        CatalogRoot? catalog = JsonSerializer.Deserialize<CatalogRoot>(json, JsonOptions);
        if (catalog?.Images == null)
            return [];

        // Prefix file names with "local:" so the import flow knows to copy from disk
        foreach (VmImage img in catalog.Images)
        {
            img.SourceType = "Local";
            img.Description = string.IsNullOrEmpty(img.Description)
                ? $"Local image from {catalogPath}"
                : img.Description;

            foreach (VmImageVersion ver in img.Versions)
            {
                string fullPath = Path.Combine(catalogPath, ver.FileName);
                ver.FileName = $"local:{fullPath}";
            }
        }

        return catalog.Images;
    }

    public Task<string> ResolveDownloadUrlAsync(
        FeedConfiguration feed,
        string versionRef,
        CancellationToken ct = default
    )
    {
        // Strip "local:" prefix if present
        VersionReference reference = VersionReference.Parse(versionRef);
        string path = reference is VersionReference.Local local ? local.FilePath : versionRef;
        return Task.FromResult(path);
    }

    public Task<bool> TestConnectivityAsync(FeedConfiguration feed, CancellationToken ct = default)
    {
        return Task.FromResult(Directory.Exists(feed.Url));
    }

    public Task<List<string>> DiscoverRepositoriesAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    )
    {
        return Task.FromResult(new List<string>());
    }

    private class CatalogRoot
    {
        public List<VmImage> Images { get; set; } = [];
    }
}
