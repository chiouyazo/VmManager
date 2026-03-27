using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Nexus;

public class NexusCatalogAdapter : ICatalogAdapter
{
    private readonly NexusCatalogService _service;
    private readonly ILogger<NexusCatalogAdapter> _logger;

    public NexusCatalogAdapter(NexusCatalogService service, ILogger<NexusCatalogAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _logger = logger;
    }

    public FeedType SupportedType => FeedType.Nexus;

    public async Task<List<VmImage>> LoadCatalogAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    )
    {
        if (!string.IsNullOrWhiteSpace(feed.Repository))
        {
            return await _service.LoadCatalogAsync(feed);
        }

        // Auto-discover: when no repository is specified, discover all raw repos and load from each
        List<string> repos = await _service.ListRawRepositoriesAsync(feed);
        List<Task<(string RepoName, List<VmImage> Images)>> repoTasks =
            new List<Task<(string RepoName, List<VmImage> Images)>>();

        foreach (string repoName in repos)
        {
            string rn = repoName;
            FeedConfiguration repoFeed = new FeedConfiguration
            {
                Id = FeedConfiguration.ComputeId(FeedType.Nexus, feed.Url, rn),
                Name = feed.Name,
                Type = FeedType.Nexus,
                Url = feed.Url,
                Repository = rn,
                Username = feed.Username,
                Password = feed.Password,
            };
            repoTasks.Add(LoadRepoImagesAsync(repoFeed, feed.Name, rn));
        }

        (string RepoName, List<VmImage> Images)[] repoResults = await Task.WhenAll(repoTasks);
        List<VmImage> allImages = new List<VmImage>();
        foreach ((string _, List<VmImage> imgs) in repoResults)
            allImages.AddRange(imgs);
        return allImages;
    }

    private async Task<(string RepoName, List<VmImage> Images)> LoadRepoImagesAsync(
        FeedConfiguration repoFeed,
        string feedName,
        string repoName
    )
    {
        try
        {
            List<VmImage> imgs = await _service.LoadCatalogAsync(repoFeed);
            List<NetworkDefinition> networks = await _service.LoadNetworksAsync(repoFeed);
            foreach (VmImage img in imgs)
            {
                img.FeedName = $"{feedName} / {repoName}";
                img.FeedId = repoFeed.Id;
                img.FeedUrl = repoFeed.Url;
                img.FeedRepository = repoName;
                img.AvailableNetworks = networks;
            }
            return (repoName, imgs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load catalog from Nexus repo {RepoName}", repoName);
            return (repoName, new List<VmImage>());
        }
    }

    public Task<string> ResolveDownloadUrlAsync(
        FeedConfiguration feed,
        string versionRef,
        CancellationToken ct = default
    )
    {
        // Nexus download URLs are embedded in the FileName as "nexus:<url>"
        return Task.FromResult(versionRef);
    }

    public Task<bool> TestConnectivityAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    ) => _service.TestConnectivityAsync(feed);

    public Task<List<string>> DiscoverRepositoriesAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    ) => _service.ListRawRepositoriesAsync(feed);
}
