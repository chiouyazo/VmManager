using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Oci;

public class OciCatalogAdapter : ICatalogAdapter
{
    private readonly OciCatalogService _service;

    public OciCatalogAdapter(OciCatalogService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public FeedType SupportedType => FeedType.OCI;

    public Task<List<VmImage>> LoadCatalogAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    ) => _service.LoadCatalogAsync(feed);

    public Task<string> ResolveDownloadUrlAsync(
        FeedConfiguration feed,
        string versionRef,
        CancellationToken ct = default
    ) => _service.GetBlobDownloadUrlAsync(feed, versionRef);

    public Task<bool> TestConnectivityAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    ) => _service.TestConnectivityAsync(feed);

    public Task<List<string>> DiscoverRepositoriesAsync(
        FeedConfiguration feed,
        CancellationToken ct = default
    ) => _service.ListRepositoriesAsync(feed);
}
