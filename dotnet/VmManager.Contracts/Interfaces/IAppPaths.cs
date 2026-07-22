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
    string UsersPath { get; }
    string VmOwnersPath { get; }
    string VmSharesPath { get; }
    string EnvironmentsPath { get; }
}
