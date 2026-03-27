using Microsoft.Extensions.DependencyInjection;
using VmManager.Catalog.Local;
using VmManager.Catalog.Nexus;
using VmManager.Catalog.Oci;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Interfaces;

namespace VmManager.Catalog;

public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogServices(this IServiceCollection services)
    {
        services.AddSingleton<OciCatalogService>();
        services.AddSingleton<NexusCatalogService>();
        services.AddSingleton<ImportService>();
        services.AddSingleton<CatalogAggregator>();
        services.AddSingleton<TarCompressor>();
        services.AddSingleton<SettingsService>();

        services.AddSingleton<ICatalogAdapter, OciCatalogAdapter>();
        services.AddSingleton<ICatalogAdapter, NexusCatalogAdapter>();
        services.AddSingleton<ICatalogAdapter, LocalCatalogAdapter>();

        services.AddSingleton<ISnapshotPushAdapter, OciPushAdapter>();
        services.AddSingleton<ISnapshotPushAdapter, NexusPushAdapter>();
        services.AddSingleton<ISnapshotPushAdapter, LocalPushAdapter>();

        return services;
    }
}
