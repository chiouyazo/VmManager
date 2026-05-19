using VmManager.Agent.Services;
using VmManager.Backends.Kvm;
using VmManager.Backends.Proxmox;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;

namespace VmManager.Agent;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentServices(
        this IServiceCollection services,
        string? backendOverride = null
    )
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<TempTracker>();
        services.AddSingleton<ITempTracker>(sp => sp.GetRequiredService<TempTracker>());
        services.AddSingleton<IVmTrackingService, VmTrackingService>();
        services.AddSingleton<ILocalImageMetadataService, LocalImageMetadataService>();
        services.AddSingleton<BackgroundTaskManager>();
        services.AddSingleton<IBackgroundTaskManager>(sp =>
            sp.GetRequiredService<BackgroundTaskManager>()
        );
        services.AddSingleton<SnapshotPushService>();
        services.AddSingleton<FeedResolutionService>();

        if (string.Equals(backendOverride, "Proxmox", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ProxmoxApiClient>(sp =>
            {
                SettingsService settings = sp.GetRequiredService<SettingsService>();
                ProxmoxSettings proxmox =
                    settings.Load().Proxmox
                    ?? throw new InvalidOperationException(
                        "Proxmox backend selected but no Proxmox settings configured in settings.json"
                    );
                return new ProxmoxApiClient(
                    proxmox,
                    sp.GetRequiredService<ILogger<ProxmoxApiClient>>()
                );
            });
            services.AddSingleton<ProxmoxIpResolver>();
            services.AddSingleton<IVmIpResolver>(sp => sp.GetRequiredService<ProxmoxIpResolver>());
        }
        else if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<VmIpResolver>();
            services.AddSingleton<IVmIpResolver>(sp => sp.GetRequiredService<VmIpResolver>());
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<KvmIpResolver>();
            services.AddSingleton<IVmIpResolver>(sp => sp.GetRequiredService<KvmIpResolver>());
        }

        services.AddSingleton<RdpTcpRelay>();
        services.AddSingleton<RdpConnectionHandler>();
        services.AddSingleton<RdpSessionStore>();
        services.AddSingleton<RdpProxyListener>();
        services.AddSingleton<NetworkTrackingService>();
        services.AddSingleton<NetworkProvisioningService>();

        services.AddSingleton<UserService>();
        services.AddSingleton<VmOwnershipService>();
        services.AddSingleton<VmSharingService>();
        services.AddSingleton<AuthorizationService>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<QuotaService>();
        services.AddHostedService<StaleVmReminderService>();

        return services;
    }
}
