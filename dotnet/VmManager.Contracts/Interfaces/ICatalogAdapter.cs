using VmManager.Contracts.Models;

namespace VmManager.Contracts.Interfaces;

public interface ICatalogAdapter
{
    FeedType SupportedType { get; }

    Task<List<VmImage>> LoadCatalogAsync(FeedConfiguration feed, CancellationToken ct = default);

    Task<string> ResolveDownloadUrlAsync(
        FeedConfiguration feed,
        string versionRef,
        CancellationToken ct = default
    );

    Task<bool> TestConnectivityAsync(FeedConfiguration feed, CancellationToken ct = default);

    Task<List<string>> DiscoverRepositoriesAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    );
}
