using Microsoft.Extensions.DependencyInjection;
using VmManager.Backends.HyperV;
using VmManager.Backends.Kvm;
using VmManager.Contracts.Interfaces;

namespace VmManager.Backends;

public static class BackendServiceCollectionExtensions
{
    public static IServiceCollection AddBackendServices(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<PowerShellRunner>();
            services.AddSingleton<HyperVWmiHelper>();
            services.AddSingleton<HyperVVmService>();
            services.AddSingleton<HyperVSnapshotService>();
            services.AddSingleton<HyperVImportService>();
            services.AddSingleton<HyperVNetworkService>();
            services.AddSingleton<INetworkService>(sp =>
                sp.GetRequiredService<HyperVNetworkService>()
            );
            services.AddSingleton<HyperVService>();
            services.AddSingleton<IVmBackend>(sp => sp.GetRequiredService<HyperVService>());
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<ShellRunner>();
            services.AddSingleton<KvmVmService>();
            services.AddSingleton<KvmSnapshotService>();
            services.AddSingleton<KvmImportService>();
            services.AddSingleton<KvmNetworkService>();
            services.AddSingleton<INetworkService>(sp =>
                sp.GetRequiredService<KvmNetworkService>()
            );
            services.AddSingleton<KvmService>();
            services.AddSingleton<IVmBackend>(sp => sp.GetRequiredService<KvmService>());
        }

        return services;
    }
}
