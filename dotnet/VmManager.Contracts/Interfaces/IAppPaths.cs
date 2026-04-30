namespace VmManager.Contracts.Interfaces;

public interface IAppPaths
{
    string AppDataDir { get; }
    string SettingsPath { get; }
    string ManagedVmsPath { get; }
    string NotesPath { get; }
    string LogDir { get; }
    string PendingCleanupPath { get; }
    string ManagedNetworksPath { get; }
    string VmAccessPath { get; }
}
