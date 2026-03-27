namespace VmManager.Services;

/// <summary>Centralises all AppData paths used by the application.</summary>
public class AppPaths : IAppPaths
{
    public string AppDataDir { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VmManager"
        );

    public string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public string ManagedVmsPath => Path.Combine(AppDataDir, "managed-vms.json");
    public string NotesPath => Path.Combine(AppDataDir, "vm-notes.json");
    public string LogDir => Path.Combine(AppDataDir, "Logs");
    public string PendingCleanupPath => Path.Combine(AppDataDir, "pending-cleanup.json");
    public string ManagedNetworksPath => Path.Combine(AppDataDir, "managed-networks.json");
}
