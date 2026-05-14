namespace VmManager.Contracts.Models;

public class AppSettings
{
    public List<FeedConfiguration> Feeds { get; set; } = new List<FeedConfiguration>();
    public bool HasCompletedSetup { get; set; }

    public string LocalVmPath { get; set; } =
        OperatingSystem.IsWindows() ? @"C:\VMs" : "/var/lib/vmmanager";
    public int DefaultMemoryMb { get; set; } = 4096;
    public int DefaultCpuCount { get; set; } = 4;

    public string DefaultLocale { get; set; } = "";
    public string DefaultKeyboardLayout { get; set; } = "";
    public string DefaultTimezone { get; set; } = "";
    public bool ApplyLocaleOnCreate { get; set; }

    public string DefaultVmUsername { get; set; } = "Administrator";
    public string DefaultVmPassword { get; set; } = "Admin123!";

    public string VmBackend { get; set; } = "HyperV";

    public bool RenameComputerToVmName { get; set; } = true;
    public string PostCreationScript { get; set; } = "";

    public bool AutoCleanupUnusedNetworks { get; set; } = true;

    public bool SecureApi { get; set; }
    public ProxmoxSettings? Proxmox { get; set; }
}
