using VmManager.Agent.Services;
using VmManager.Backends.Kvm;

namespace VmManager.Agent;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
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
        services.AddSingleton<PreflightService>();
        services.AddSingleton<SnapshotPushService>();
        services.AddSingleton<FeedResolutionService>();
        if (OperatingSystem.IsWindows())
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
        services.AddSingleton<VmAccessStore>();
        services.AddSingleton<VmAuthorizationService>();
        services.AddSingleton<TunnelSessionStore>();
        return services;
    }
}
