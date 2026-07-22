using VmManager.Contracts.Interfaces;

namespace VmManager.Tests.Environments;

/// <summary>IAppPaths rooted at a throwaway temp directory for isolated tests.</summary>
public sealed class TestAppPaths : IAppPaths, IDisposable
{
    public TestAppPaths()
    {
        AppDataDir = Path.Combine(Path.GetTempPath(), $"vmm_env_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(AppDataDir);
    }

    public string AppDataDir { get; }
    public string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public string ManagedVmsPath => Path.Combine(AppDataDir, "managed-vms.json");
    public string NotesPath => Path.Combine(AppDataDir, "vm-notes.json");
    public string LogDir => Path.Combine(AppDataDir, "Logs");
    public string PendingCleanupPath => Path.Combine(AppDataDir, "pending-cleanup.json");
    public string ManagedNetworksPath => Path.Combine(AppDataDir, "managed-networks.json");
    public string UsersPath => Path.Combine(AppDataDir, "users.json");
    public string VmOwnersPath => Path.Combine(AppDataDir, "vm-owners.json");
    public string VmSharesPath => Path.Combine(AppDataDir, "vm-shares.json");
    public string EnvironmentsPath => Path.Combine(AppDataDir, "environments.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(AppDataDir))
                Directory.Delete(AppDataDir, true);
        }
        catch
        {
            // best effort
        }
    }
}
