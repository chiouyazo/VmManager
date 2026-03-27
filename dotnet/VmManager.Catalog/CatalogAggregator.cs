using Microsoft.Extensions.Logging;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog;

public class CatalogAggregator
{
    private readonly IEnumerable<ICatalogAdapter> _adapters;
    private readonly SettingsService _settingsService;
    private readonly ILogger<CatalogAggregator> _logger;

    public CatalogAggregator(
        IEnumerable<ICatalogAdapter> adapters,
        SettingsService settingsService,
        ILogger<CatalogAggregator> logger
    )
    {
        _adapters = adapters;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>Loads images from all configured sources, merged.</summary>
    public async Task<List<VmImage>> LoadCatalogAsync()
    {
        AppSettings settings = await _settingsService.LoadAsync();

        _logger.LogInformation(
            "Loading catalog from {FeedCount} configured feeds",
            settings.Feeds.Count
        );

        List<Task<(FeedConfiguration Feed, string FeedName, List<VmImage> Images)>> tasks =
            new List<Task<(FeedConfiguration Feed, string FeedName, List<VmImage> Images)>>();

        foreach (FeedConfiguration feed in settings.Feeds)
        {
            _logger.LogDebug(
                "Processing feed {FeedName} ({FeedType}) at {FeedUrl}",
                feed.Name,
                feed.Type,
                feed.Url
            );
            ICatalogAdapter? adapter = _adapters.FirstOrDefault(a => a.SupportedType == feed.Type);
            if (adapter != null)
            {
                FeedConfiguration captured = feed;
                string feedName =
                    feed.Type == FeedType.Nexus && !string.IsNullOrWhiteSpace(feed.Repository)
                        ? $"{feed.Name} / {feed.Repository}"
                        : feed.Name;
                tasks.Add(
                    SafeLoadAsync(captured, feedName, () => adapter.LoadCatalogAsync(captured))
                );
            }
        }

        (FeedConfiguration Feed, string FeedName, List<VmImage> Images)[] results =
            await Task.WhenAll(tasks);
        List<VmImage> all = new List<VmImage>();
        foreach ((FeedConfiguration feed2, string feedName, List<VmImage> images) in results)
        {
            foreach (VmImage img in images)
            {
                if (string.IsNullOrEmpty(img.FeedName))
                    img.FeedName = feedName;
                if (string.IsNullOrEmpty(img.FeedId))
                    img.FeedId = feed2.Id;
                if (string.IsNullOrEmpty(img.FeedUrl))
                    img.FeedUrl = feed2.Url;
                if (string.IsNullOrEmpty(img.FeedRepository))
                    img.FeedRepository = feed2.Repository;
            }
            all.AddRange(images);
        }

        async Task<(FeedConfiguration Feed, string FeedName, List<VmImage> Images)> SafeLoadAsync(
            FeedConfiguration feedConfig,
            string feedName,
            Func<Task<List<VmImage>>> loader
        )
        {
            try
            {
                List<VmImage> images = await loader();
                _logger.LogInformation(
                    "Feed {FeedName} loaded {ImageCount} images",
                    feedName,
                    images.Count
                );
                return (feedConfig, feedName, images);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load feed {FeedName}", feedName);
                return (feedConfig, feedName, new List<VmImage>());
            }
        }

        // Link versions back to their parent image and merge snapshots
        foreach (VmImage img in all)
        {
            foreach (VmImageVersion ver in img.Versions)
            {
                ver.ParentImageId = img.Id;
                ver.ParentImageName = img.Name;
                ver.FeedId = img.FeedId;
                ver.FeedUrl = img.FeedUrl;
                ver.FeedRepository = img.FeedRepository;
            }

            foreach (VmImageVersion snap in img.UserSnapshots)
            {
                snap.ParentImageId = img.Id;
                snap.ParentImageName = img.Name;
                snap.FeedId = img.FeedId;
                snap.FeedUrl = img.FeedUrl;
                snap.FeedRepository = img.FeedRepository;
            }
        }

        // Attach orphaned snapshots (snapshots whose parent image is in a different backend)
        Dictionary<string, VmImage> byId = all.Where(i => i.Versions.Count > 0)
            .ToDictionary(i => i.Id, i => i);
        List<VmImage> orphaned = all.Where(i => i.Versions.Count == 0 && i.UserSnapshots.Count > 0)
            .ToList();
        foreach (VmImage snapImage in orphaned)
        {
            string parentId = snapImage.UserSnapshots.FirstOrDefault()?.ParentImageId ?? "";
            if (!string.IsNullOrEmpty(parentId) && byId.TryGetValue(parentId, out VmImage? parent))
            {
                parent.UserSnapshots.AddRange(snapImage.UserSnapshots);
                all.Remove(snapImage);
            }
        }

        return all;
    }

    /// <summary>Alias for pages that call GetImagesAsync.</summary>
    public Task<List<VmImage>> GetImagesAsync() => LoadCatalogAsync();

    /// <summary>
    /// Returns the download path/URL for a version.
    /// Local versions use "local:{filePath}" prefix, OCI uses "repo:tag", Nexus uses "nexus:{url}".
    /// </summary>
    public async Task<string> GetDownloadUrlAsync(string versionFileName)
    {
        VersionReference reference = VersionReference.Parse(versionFileName);

        switch (reference)
        {
            case VersionReference.Local local:
                return local.FilePath;
            case VersionReference.Nexus nexus:
                return nexus.DownloadUrl;
            case VersionReference.Oci oci:
            {
                AppSettings settings = _settingsService.Load();
                FeedConfiguration? ociFeed = settings.Feeds.FirstOrDefault(f =>
                    f.Type == FeedType.OCI
                );
                if (ociFeed == null)
                    throw new InvalidOperationException("No OCI feed configured");

                ICatalogAdapter? ociAdapter = _adapters.FirstOrDefault(a =>
                    a.SupportedType == FeedType.OCI
                );
                if (ociAdapter == null)
                    throw new InvalidOperationException("No OCI adapter registered");

                string tag = oci.RepositoryTag.Contains(':')
                    ? oci.RepositoryTag.Split(':').Last()
                    : oci.RepositoryTag;
                return await ociAdapter.ResolveDownloadUrlAsync(ociFeed, tag);
            }
            default:
                throw new InvalidOperationException(
                    $"Unknown version reference type: {reference.GetType().Name}"
                );
        }
    }

    public static bool IsLocalVersion(string versionFileName) =>
        VersionReference.Parse(versionFileName).IsLocal;

    public static bool IsNexusVersion(string versionFileName) =>
        VersionReference.Parse(versionFileName).IsNexus;

    /// <summary>Returns auth header for the first OCI feed.</summary>
    public System.Net.Http.Headers.AuthenticationHeaderValue? GetAuthHeader()
    {
        AppSettings settings = _settingsService.Load();
        FeedConfiguration? ociFeed = settings.Feeds.FirstOrDefault(f => f.Type == FeedType.OCI);
        return ociFeed != null ? AuthHelper.BuildBasicAuth(ociFeed) : null;
    }

    /// <summary>Returns auth header for the first Nexus feed.</summary>
    public System.Net.Http.Headers.AuthenticationHeaderValue? GetNexusAuthHeader()
    {
        AppSettings settings = _settingsService.Load();
        FeedConfiguration? nexusFeed = settings.Feeds.FirstOrDefault(f => f.Type == FeedType.Nexus);
        return nexusFeed != null ? AuthHelper.BuildBasicAuth(nexusFeed) : null;
    }

    public bool IsAnySourceConfigured()
    {
        AppSettings settings = _settingsService.Load();
        return settings.Feeds.Count > 0;
    }
}
