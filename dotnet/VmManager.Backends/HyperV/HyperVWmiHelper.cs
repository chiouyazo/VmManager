using System.Management;
using Microsoft.Extensions.Logging;

namespace VmManager.Backends.HyperV;

/// <summary>
/// Encapsulates WMI access to the Hyper-V virtualization namespace.
/// </summary>
public class HyperVWmiHelper
{
    private readonly ILogger<HyperVWmiHelper> _logger;
    private ManagementScope? _scope;

    public HyperVWmiHelper(ILogger<HyperVWmiHelper> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Gets (or creates) a connected WMI scope for root\virtualization\v2.</summary>
    public ManagementScope Scope
    {
        get
        {
            if (_scope?.IsConnected != true)
            {
                _scope = new ManagementScope(@"\\.\root\virtualization\v2");
                _scope.Connect();
            }
            return _scope;
        }
    }

    /// <summary>Finds a VM by name via WMI, or returns null.</summary>
    public ManagementObject? GetVm(string name)
    {
        // Don't filter by Caption - it's localized (e.g. 'Virtueller Computer' on German Windows).
        // Instead filter by ElementName and exclude the host management OS (Description='Microsoft Hosting Computer System').
        SelectQuery query = new SelectQuery(
            "Msvm_ComputerSystem",
            $"ElementName='{name.Replace("'", "''")}'"
        );
        using ManagementObjectSearcher searcher = new ManagementObjectSearcher(Scope, query);
        return searcher
            .Get()
            .Cast<ManagementObject>()
            .FirstOrDefault(o => (string?)o["Description"] != "Microsoft Hosting Computer System");
    }

    /// <summary>Gets the Msvm_VirtualSystemManagementService singleton.</summary>
    public ManagementObject GetManagementService()
    {
        using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
            Scope,
            new SelectQuery("Msvm_VirtualSystemManagementService")
        );
        return searcher.Get().Cast<ManagementObject>().First();
    }

    /// <summary>Gets the Msvm_VirtualSystemSnapshotService singleton.</summary>
    public ManagementObject GetSnapshotService()
    {
        using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
            Scope,
            new SelectQuery("Msvm_VirtualSystemSnapshotService")
        );
        return searcher.Get().Cast<ManagementObject>().First();
    }

    /// <summary>Gets the VirtualSystemSettingData for a VM.</summary>
    public ManagementObject GetVmSettings(ManagementObject vm)
    {
        RelatedObjectQuery settingsQuery = new RelatedObjectQuery(
            vm.Path.Path,
            "Msvm_VirtualSystemSettingData"
        );
        using ManagementObjectSearcher settingsSearcher = new ManagementObjectSearcher(
            Scope,
            settingsQuery
        );
        return settingsSearcher
                .Get()
                .Cast<ManagementObject>()
                .FirstOrDefault(s =>
                    (string)s["VirtualSystemType"] == "Microsoft:Hyper-V:System:Realized"
                )
            ?? settingsSearcher.Get().Cast<ManagementObject>().First();
    }

    /// <summary>Gets memory usage (in bytes) for a running VM.</summary>
    public long GetMemoryUsage(ManagementObject vm)
    {
        try
        {
            RelatedObjectQuery memQuery = new RelatedObjectQuery(
                vm.Path.Path,
                "Msvm_MemorySettingData"
            );
            using ManagementObjectSearcher memSearcher = new ManagementObjectSearcher(
                Scope,
                memQuery
            );
            ManagementObject? memObj = memSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (memObj != null)
            {
                object? limit = memObj["Limit"];
                if (limit != null)
                    return (long)(ulong)limit * 1024 * 1024; // MB to bytes
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query memory usage for VM");
        }
        return 0;
    }

    /// <summary>Waits for a WMI async job to complete. Throws on failure.</summary>
    public static void WaitForJob(ManagementBaseObject result)
    {
        uint retVal = (uint)result["ReturnValue"];
        if (retVal == 0)
            return; // Completed synchronously
        if (retVal != 4096)
            throw new InvalidOperationException(
                $"WMI operation failed with return value {retVal}."
            );

        string? jobPath = (string)result["Job"];
        using ManagementObject job = new ManagementObject(jobPath);
        while (true)
        {
            job.Get();
            ushort jobState = (ushort)job["JobState"];
            if (jobState == 7)
                return; // Completed
            if (jobState is 8 or 9 or 10) // Exception, Terminated, Killed
            {
                string error = (string?)job["ErrorDescription"] ?? "Unknown WMI job error.";
                throw new InvalidOperationException(error);
            }
            Thread.Sleep(50);
        }
    }

    /// <summary>Maps a WMI EnabledState value to a friendly string.</summary>
    public static string MapWmiState(ushort state) =>
        state switch
        {
            2 => "Running",
            3 => "Off",
            6 or 32769 => "Saved",
            9 or 32770 => "Starting",
            4 or 32768 => "Paused",
            10 or 32773 => "Stopping",
            _ => $"Unknown ({state})",
        };
}
