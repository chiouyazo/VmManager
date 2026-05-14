using Microsoft.Extensions.DependencyInjection;
using VmManager.Backends.HyperV;
using VmManager.Backends.Kvm;
using VmManager.Backends.Proxmox;
using VmManager.Contracts.Interfaces;

namespace VmManager.Backends;

public static class BackendServiceCollectionExtensions
{
    public static IServiceCollection AddBackendServices(
        this IServiceCollection services,
        string? backendOverride = null
    )
    {
        if (string.Equals(backendOverride, "Proxmox", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ShellRunner>();
            services.AddSingleton<ProxmoxVmService>();
            services.AddSingleton<ProxmoxSnapshotService>();
            services.AddSingleton<ProxmoxImportService>();
            services.AddSingleton<ProxmoxNetworkService>();
            services.AddSingleton<INetworkService>(sp =>
                sp.GetRequiredService<ProxmoxNetworkService>()
            );
            services.AddSingleton<ProxmoxPreflightService>();
            services.AddSingleton<IPreflightService>(sp =>
                sp.GetRequiredService<ProxmoxPreflightService>()
            );
            services.AddSingleton<ProxmoxService>();
            services.AddSingleton<IVmBackend>(sp => sp.GetRequiredService<ProxmoxService>());
        }
        else if (OperatingSystem.IsWindows())
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
            services.AddSingleton<HyperVPreflightService>();
            services.AddSingleton<IPreflightService>(sp =>
                sp.GetRequiredService<HyperVPreflightService>()
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
            services.AddSingleton<KvmPreflightService>();
            services.AddSingleton<IPreflightService>(sp =>
                sp.GetRequiredService<KvmPreflightService>()
            );
            services.AddSingleton<KvmService>();
            services.AddSingleton<IVmBackend>(sp => sp.GetRequiredService<KvmService>());
        }

        return services;
    }
}
