namespace VmManager.Models;

/// <summary>User-configurable settings persisted in AppData.</summary>
public class AppSettings
{
    // ── Registry ─────────────────────────────────────────────────────────

    /// <summary>OCI registry URL (e.g. "https://registry.example.com").</summary>
    public string RegistryUrl { get; set; } = "";

    /// <summary>OCI repository path (e.g. "vagrant/myboxes").</summary>
    public string RegistryRepository { get; set; } = "";

    /// <summary>Optional registry username for basic auth.</summary>
    public string RegistryUsername { get; set; } = "";

    /// <summary>Optional registry password for basic auth.</summary>
    public string RegistryPassword { get; set; } = "";

    // ── Local catalog ──────────────────────────────────────────────────

    /// <summary>Path to a local/network folder containing catalog.json and .box files.</summary>
    public string LocalCatalogPath { get; set; } = "";

    // ── Local storage ───────────────────────────────────────────────────

    /// <summary>Local folder where VM files are copied and registered with Hyper-V.</summary>
    public string LocalVmPath { get; set; } = @"C:\VMs";

    public int DefaultMemoryMb { get; set; } = 4096;
    public int DefaultCpuCount { get; set; } = 4;

    // ── VM Credentials ───────────────────────────────────────────────────

    /// <summary>Default username shown in VM Connect and stored via cmdkey for auto-login.</summary>
    public string DefaultVmUsername { get; set; } = "Administrator";

    /// <summary>Default password pre-filled from the Packer build (var.admin_password default).</summary>
    public string DefaultVmPassword { get; set; } = "Admin123!";

    // ── Nexus ─────────────────────────────────────────────────────────────

    /// <summary>Nexus raw repository URL (e.g. "https://nexus.example.com").</summary>
    public string NexusUrl { get; set; } = "";

    /// <summary>Nexus username for basic auth.</summary>
    public string NexusUsername { get; set; } = "";

    /// <summary>Nexus password for basic auth.</summary>
    public string NexusPassword { get; set; } = "";

    /// <summary>Nexus repository name (e.g. "Release_LinuxImages").</summary>
    public string NexusRepository { get; set; } = "";

    /// <summary>Returns true if Nexus is configured.</summary>
    public bool IsNexusConfigured =>
        !string.IsNullOrWhiteSpace(NexusUrl) && !string.IsNullOrWhiteSpace(NexusRepository);

    // ── Docker backend ───────────────────────────────────────────────────

    /// <summary>Preferred VM backend: "HyperV" (default) or "Docker".</summary>
    public string VmBackend { get; set; } = "HyperV";

    /// <summary>Remote Docker host connection string.</summary>
    public string DockerHost { get; set; } = "";

    /// <summary>Returns true if the registry is configured.</summary>
    public bool IsRegistryConfigured =>
        !string.IsNullOrWhiteSpace(RegistryUrl) && !string.IsNullOrWhiteSpace(RegistryRepository);
}
