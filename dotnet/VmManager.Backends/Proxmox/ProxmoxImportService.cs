using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VmManager.Backends.Kvm;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Proxmox;

public class ProxmoxImportService
{
    private readonly ProxmoxApiClient _api;
    private readonly ProxmoxVmService _vms;
    private readonly ShellRunner _sh;
    private readonly IVmIpResolver _ipResolver;
    private readonly ILogger<ProxmoxImportService> _logger;

    public ProxmoxImportService(
        ProxmoxApiClient api,
        ProxmoxVmService vms,
        ShellRunner sh,
        IVmIpResolver ipResolver,
        ILogger<ProxmoxImportService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(vms);
        ArgumentNullException.ThrowIfNull(sh);
        ArgumentNullException.ThrowIfNull(ipResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _api = api;
        _vms = vms;
        _sh = sh;
        _ipResolver = ipResolver;
        _logger = logger;
    }

    public async Task ImportVmAsync(
        string extractedFolder,
        string localVmPath,
        int memoryMb,
        int cpuCount,
        string? vmName = null,
        bool skipDefaultNetwork = false,
        Action<string>? onStatus = null,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Importing VM {VmName} from {Folder} (Memory={Mb}MB, CPU={Cpu})",
            vmName ?? "auto",
            extractedFolder,
            memoryMb,
            cpuCount
        );

        string? diskPath = Directory
            .GetFiles(extractedFolder, "*.qcow2", SearchOption.AllDirectories)
            .FirstOrDefault();
        string? vhdxPath = null;
        if (diskPath == null)
        {
            vhdxPath = Directory
                .GetFiles(extractedFolder, "*.vhdx", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(extractedFolder, "*.avhdx", SearchOption.AllDirectories))
                .FirstOrDefault();
            if (vhdxPath == null)
                throw new FileNotFoundException(
                    $"No .qcow2 or .vhdx file found in: {extractedFolder}"
                );
        }

        if (vhdxPath != null)
        {
            onStatus?.Invoke("Converting VHDX to QCOW2...");
            diskPath = Path.ChangeExtension(vhdxPath, ".qcow2");
            await _sh.RunBashAsync($"qemu-img convert -W -O qcow2 {Q(vhdxPath)} {Q(diskPath)}");
        }

        string folderName = Path.GetFileName(extractedFolder);
        string baseName = vmName ?? Regex.Replace(folderName, @"-\d+$", "");
        if (string.IsNullOrEmpty(baseName))
            baseName = folderName;

        List<VmInstance> existing = await _vms.GetVmsAsync();
        HashSet<string> taken = new(existing.Select(v => v.Name));
        string finalName = baseName;
        int counter = 2;
        while (taken.Contains(finalName))
        {
            finalName = baseName + "-" + counter;
            counter++;
        }

        if (_api.MaxPoolMemoryMb > 0 || _api.MaxPoolCpuCores > 0)
        {
            onStatus?.Invoke("Checking pool resource limits...");
            JsonElement pool = await _api.GetAsync<JsonElement>($"/api2/json/pools/{_api.PoolId}");
            if (pool.TryGetProperty("members", out JsonElement members))
            {
                long totalMemMb = 0;
                int totalCores = 0;
                foreach (JsonElement m in members.EnumerateArray())
                {
                    if (m.TryGetProperty("type", out JsonElement t) && t.GetString() != "qemu")
                        continue;
                    totalMemMb += m.TryGetProperty("maxmem", out JsonElement mm)
                        ? mm.GetInt64() / 1024 / 1024
                        : 0;
                    totalCores += m.TryGetProperty("maxcpu", out JsonElement mc)
                        ? mc.GetInt32()
                        : 0;
                }

                if (_api.MaxPoolMemoryMb > 0 && totalMemMb + memoryMb > _api.MaxPoolMemoryMb)
                    throw new InvalidOperationException(
                        $"Pool memory limit exceeded. Current: {totalMemMb} MB + new VM: {memoryMb} MB, Limit: {_api.MaxPoolMemoryMb} MB."
                    );

                if (_api.MaxPoolCpuCores > 0 && totalCores + cpuCount > _api.MaxPoolCpuCores)
                    throw new InvalidOperationException(
                        $"Pool CPU limit exceeded. Current: {totalCores} cores + new VM: {cpuCount} cores, Limit: {_api.MaxPoolCpuCores} cores."
                    );
            }
        }

        onStatus?.Invoke("Allocating VM ID...");
        int vmid = await _api.GetNextVmIdAsync();
        _logger.LogInformation("Creating VM {Name} with VMID {VmId}", finalName, vmid);

        onStatus?.Invoke("Detecting storage type...");
        string diskFormat = "qcow2";
        try
        {
            JsonElement storages = await _api.GetAsync<JsonElement>(
                $"/api2/json/nodes/{_api.Node}/storage"
            );
            if (storages.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement s in storages.EnumerateArray())
                {
                    string sid = s.TryGetProperty("storage", out JsonElement sidEl)
                        ? sidEl.GetString() ?? ""
                        : "";
                    if (!string.Equals(sid, _api.StorageId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string storageType = s.TryGetProperty("type", out JsonElement typeEl)
                        ? typeEl.GetString() ?? ""
                        : "";
                    if (storageType is "lvmthin" or "lvm" or "zfspool" or "rbd")
                        diskFormat = "raw";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to detect storage type for '"
                    + _api.StorageId
                    + "'. Cannot determine disk format. Check that the API token has access to the node storage list.",
                ex
            );
        }

        _logger.LogInformation(
            "Storage {StorageId} detected as format: {Format}",
            _api.StorageId,
            diskFormat
        );

        onStatus?.Invoke("Creating VM...");
        string bridge = _api.DefaultBridge;
        string networkParam = skipDefaultNetwork ? "none" : "e1000e,bridge=" + bridge;
        Dictionary<string, string> createParams = new Dictionary<string, string>
        {
            ["vmid"] = vmid.ToString(),
            ["name"] = finalName,
            ["memory"] = memoryMb.ToString(),
            ["cores"] = cpuCount.ToString(),
            ["sockets"] = "1",
            ["cpu"] = "host",
            ["ostype"] = "win10",
            ["machine"] = "q35",
            ["bios"] = "ovmf",
            ["efidisk0"] = $"{_api.StorageId}:1,efitype=4m,pre-enrolled-keys=0,format={diskFormat}",
            ["agent"] = "1",
            ["pool"] = _api.PoolId,
        };
        if (!skipDefaultNetwork)
            createParams["net0"] = networkParam;

        string createRaw = await _api.PostRawAsync(
            $"/api2/json/nodes/{_api.Node}/qemu",
            createParams
        );
        string createUpid = JsonDocument
            .Parse(createRaw)
            .RootElement.GetProperty("data")
            .GetString()!;
        await _api.PollTaskAsync(createUpid);

        if (_api.ImportMethod == "DiskPassthrough")
            await ImportViaDiskPassthroughAsync(diskPath!, diskFormat, vmid, onStatus);
        else
            await ImportViaStandardAsync(diskPath!, diskFormat, vmid, onStatus);

        onStatus?.Invoke("VM imported successfully");
        _logger.LogInformation("VM created: {Name} (VMID {VmId})", finalName, vmid);
    }

    private async Task ImportViaStandardAsync(
        string diskPath,
        string diskFormat,
        int vmid,
        Action<string>? onStatus
    )
    {
        onStatus?.Invoke("Importing disk image...");
        await _sh.RunBashAsync(
            $"qm importdisk {vmid} {Q(diskPath)} {_api.StorageId} --format {diskFormat}"
        );

        onStatus?.Invoke("Configuring VM...");
        JsonElement vmConfig = await _api.GetAsync<JsonElement>($"{_api.VmPath(vmid)}/config");
        string? diskVolId = null;
        for (int i = 0; i < 16; i++)
        {
            string key = "unused" + i;
            if (vmConfig.TryGetProperty(key, out JsonElement unusedEl))
            {
                diskVolId = unusedEl.GetString();
                break;
            }
        }
        if (diskVolId == null)
            throw new InvalidOperationException("No unused disk found after import.");

        _logger.LogInformation("Attaching disk volume: {VolId}", diskVolId);
        await _api.PutAsync(
            $"{_api.VmPath(vmid)}/config",
            new Dictionary<string, string> { ["sata0"] = diskVolId + ",ssd=1,discard=on" }
        );
        await _api.PutAsync(
            $"{_api.VmPath(vmid)}/config",
            new Dictionary<string, string> { ["boot"] = "order=sata0" }
        );
    }

    private async Task ImportViaDiskPassthroughAsync(
        string diskPath,
        string diskFormat,
        int targetVmId,
        Action<string>? onStatus
    )
    {
        int agentVmId = _api.AgentVmId;
        if (agentVmId <= 0)
            throw new InvalidOperationException(
                "DiskPassthrough requires AgentVmId to be set in Proxmox settings."
            );

        onStatus?.Invoke("Reading disk image size...");
        string infoJson = await _sh.RunBashAsync($"qemu-img info --output=json {Q(diskPath)}");
        long virtualSizeBytes = JsonDocument
            .Parse(infoJson)
            .RootElement.GetProperty("virtual-size")
            .GetInt64();
        int sizeGb = (int)Math.Ceiling(virtualSizeBytes / (1024.0 * 1024.0 * 1024.0));
        _logger.LogInformation("Disk virtual size: {SizeGb} GB", sizeGb);

        onStatus?.Invoke("Finding free SCSI slot on agent VM...");
        JsonElement agentConfig = await _api.GetAsync<JsonElement>(
            $"{_api.VmPath(agentVmId)}/config"
        );
        string scsiSlot = "";
        for (int i = 1; i <= 13; i++)
        {
            string candidate = "scsi" + i;
            if (!agentConfig.TryGetProperty(candidate, out _))
            {
                scsiSlot = candidate;
                break;
            }
        }
        if (string.IsNullOrEmpty(scsiSlot))
            throw new InvalidOperationException("No free SCSI slot on agent VM.");

        onStatus?.Invoke("Snapshotting block devices...");
        string devicesBefore = await _sh.RunBashAsync("lsblk -dno NAME");
        HashSet<string> beforeSet = new HashSet<string>(
            devicesBefore.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );

        string volumeId = "";
        try
        {
            onStatus?.Invoke("Creating temporary disk on agent VM...");
            await _api.PutAsync(
                $"{_api.VmPath(agentVmId)}/config",
                new Dictionary<string, string> { [scsiSlot] = $"{_api.StorageId}:{sizeGb}" }
            );

            JsonElement agentConfigAfterCreate = await _api.GetAsync<JsonElement>(
                $"{_api.VmPath(agentVmId)}/config"
            );
            string scsiValue = agentConfigAfterCreate.GetProperty(scsiSlot).GetString()!;
            volumeId = scsiValue.Split(',')[0];
            _logger.LogInformation("Created temp disk: {VolumeId}", volumeId);

            onStatus?.Invoke("Waiting for disk to appear...");
            await _sh.RunBashAsync(
                "for host in /sys/class/scsi_host/host*; do echo \"- - -\" > \"$host/scan\"; done"
            );

            string newDevice = "";
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(1000);
                string devicesNow = await _sh.RunBashAsync("lsblk -dno NAME");
                foreach (
                    string dev in devicesNow.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                )
                {
                    if (!beforeSet.Contains(dev))
                    {
                        newDevice = dev;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(newDevice))
                    break;
            }

            if (string.IsNullOrEmpty(newDevice))
                throw new TimeoutException("Temporary disk did not appear within 30 seconds.");

            _logger.LogInformation("New device detected: /dev/{Device}", newDevice);

            onStatus?.Invoke("Writing disk image to temporary disk...");
            await _sh.RunBashAsync($"qemu-img convert -O raw {Q(diskPath)} /dev/{newDevice}");

            onStatus?.Invoke("Detaching temporary disk from agent VM...");
            await _api.PutAsync(
                $"{_api.VmPath(agentVmId)}/config",
                new Dictionary<string, string> { ["delete"] = scsiSlot }
            );

            onStatus?.Invoke("Moving disk to target VM...");
            JsonElement agentConfigAfterDetach = await _api.GetAsync<JsonElement>(
                $"{_api.VmPath(agentVmId)}/config"
            );
            string unusedSlot = "";
            for (int i = 0; i < 16; i++)
            {
                string key = "unused" + i;
                if (
                    agentConfigAfterDetach.TryGetProperty(key, out JsonElement unusedEl)
                    && unusedEl.GetString() == volumeId
                )
                {
                    unusedSlot = key;
                    break;
                }
            }
            if (string.IsNullOrEmpty(unusedSlot))
                throw new InvalidOperationException(
                    "Detached disk not found as unused on agent VM. Volume: " + volumeId
                );

            string moveRaw = await _api.PostRawAsync(
                $"{_api.VmPath(agentVmId)}/move_disk",
                new Dictionary<string, string>
                {
                    ["disk"] = unusedSlot,
                    ["target-vmid"] = targetVmId.ToString(),
                    ["target-disk"] = "sata0",
                }
            );
            string moveUpid = JsonDocument
                .Parse(moveRaw)
                .RootElement.GetProperty("data")
                .GetString()!;
            await _api.PollTaskAsync(moveUpid);

            onStatus?.Invoke("Setting boot order...");
            await _api.PutAsync(
                $"{_api.VmPath(targetVmId)}/config",
                new Dictionary<string, string> { ["boot"] = "order=sata0" }
            );
        }
        catch
        {
            _logger.LogWarning("DiskPassthrough failed, cleaning up...");
            try
            {
                JsonElement cleanupConfig = await _api.GetAsync<JsonElement>(
                    $"{_api.VmPath(agentVmId)}/config"
                );
                if (cleanupConfig.TryGetProperty(scsiSlot, out _))
                {
                    await _api.PutAsync(
                        $"{_api.VmPath(agentVmId)}/config",
                        new Dictionary<string, string> { ["delete"] = scsiSlot }
                    );
                }

                if (!string.IsNullOrEmpty(volumeId))
                {
                    cleanupConfig = await _api.GetAsync<JsonElement>(
                        $"{_api.VmPath(agentVmId)}/config"
                    );
                    for (int i = 0; i < 16; i++)
                    {
                        string key = "unused" + i;
                        if (
                            cleanupConfig.TryGetProperty(key, out JsonElement unusedEl)
                            && unusedEl.GetString() == volumeId
                        )
                        {
                            await _api.PostRawAsync(
                                $"{_api.VmPath(agentVmId)}/unlink",
                                new Dictionary<string, string> { ["idlist"] = key, ["force"] = "1" }
                            );
                            break;
                        }
                    }
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Cleanup after failed DiskPassthrough also failed");
            }
            throw;
        }
    }

    public async Task ConfigureLocaleAsync(
        string vmName,
        string username,
        string password,
        string locale,
        string keyboardLayout,
        string timezone,
        Action<string>? onStatus = null
    )
    {
        _logger.LogInformation(
            "Configuring locale for VM {VmName}: {Locale}, keyboard={Keyboard}, tz={Timezone}",
            vmName,
            locale,
            keyboardLayout,
            timezone
        );

        if (string.IsNullOrWhiteSpace(keyboardLayout))
            keyboardLayout = "00000409";

        CultureInfo culture = CultureInfo.GetCultureInfo(locale);
        string lcidHex4 = culture.LCID.ToString("x4");
        string inputMethodTip = $"{lcidHex4}:{keyboardLayout}";

        onStatus?.Invoke("Starting VM for locale configuration...");
        int vmid = await _vms.ResolveVmIdAsync(vmName);
        JsonElement status = await _api.GetAsync<JsonElement>(
            $"{_api.VmPath(vmid)}/status/current"
        );
        if (status.GetProperty("status").GetString() != "running")
        {
            string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/start");
            await _api.PollTaskAsync(upid);
        }

        onStatus?.Invoke("Waiting for VM to get IP address...");
        string? ip = await WaitForIpAsync(vmName, TimeSpan.FromMinutes(5));
        if (ip == null)
        {
            await StopVmAsync(vmid);
            throw new TimeoutException(
                $"VM '{vmName}' did not receive an IP address within 5 minutes."
            );
        }

        _logger.LogInformation("VM {VmName} has IP {Ip}, applying locale via WinRM", vmName, ip);

        onStatus?.Invoke("Applying language and keyboard settings...");
        string localeScript = Shared.WinRmLocaleHelper.BuildLocaleScript(
            locale,
            keyboardLayout,
            inputMethodTip,
            timezone
        );
        await Shared.WinRmLocaleHelper.RunWinRmPowerShellAsync(
            ip,
            username,
            password,
            localeScript
        );

        onStatus?.Invoke("Rebooting VM to apply changes...");
        try
        {
            await Shared.WinRmLocaleHelper.RunWinRmPowerShellAsync(
                ip,
                username,
                password,
                "Restart-Computer -Force"
            );
        }
        catch { }

        onStatus?.Invoke("Re-applying keyboard after reboot...");
        await Task.Delay(TimeSpan.FromSeconds(15));
        ip = await WaitForIpAsync(vmName, TimeSpan.FromMinutes(3));
        if (ip != null)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            string reapply = Shared.WinRmLocaleHelper.BuildReapplyScript(
                locale,
                keyboardLayout,
                inputMethodTip
            );
            try
            {
                await Shared.WinRmLocaleHelper.RunWinRmPowerShellAsync(
                    ip,
                    username,
                    password,
                    reapply
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-apply keyboard after reboot");
            }
        }

        onStatus?.Invoke("Shutting down VM...");
        await StopVmAsync(vmid);
    }

    public async Task RunPostCreationAsync(
        string vmName,
        string username,
        string password,
        bool renameComputer,
        string? postCreationScript = null,
        Action<string>? onStatus = null
    )
    {
        if (!renameComputer && string.IsNullOrWhiteSpace(postCreationScript))
            return;

        int vmid = await _vms.ResolveVmIdAsync(vmName);
        JsonElement status = await _api.GetAsync<JsonElement>(
            $"{_api.VmPath(vmid)}/status/current"
        );
        if (status.GetProperty("status").GetString() != "running")
        {
            string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/start");
            await _api.PollTaskAsync(upid);
        }

        onStatus?.Invoke("Waiting for VM to get IP address...");
        string? ip = await WaitForIpAsync(vmName, TimeSpan.FromMinutes(5));
        if (ip == null)
        {
            await StopVmAsync(vmid);
            throw new TimeoutException($"VM '{vmName}' did not receive an IP within 5 minutes.");
        }

        try
        {
            onStatus?.Invoke("Running post-creation tasks...");
            await Shared.WinRmLocaleHelper.RunPostCreationAsync(
                ip,
                username,
                password,
                vmName,
                renameComputer,
                postCreationScript
            );
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Post-creation tasks failed for VM {VmName}", vmName);
        }

        await StopVmAsync(vmid);
    }

    public async Task ConfigureAndFinalizeAsync(
        string vmName,
        string username,
        string password,
        string? locale,
        string? keyboardLayout,
        string? timezone,
        bool renameComputer,
        string? postCreationScript,
        Action<string>? onStatus = null
    )
    {
        bool needsLocale =
            !string.IsNullOrWhiteSpace(locale) && !string.IsNullOrWhiteSpace(keyboardLayout);
        bool needsPostCreation = renameComputer || !string.IsNullOrWhiteSpace(postCreationScript);

        if (!needsLocale && !needsPostCreation)
        {
            onStatus?.Invoke("Creating base snapshot...");
            await CreateBaseSnapshotAsync(vmName);
            return;
        }

        string? inputMethodTip = null;
        if (needsLocale)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(locale!);
            string lcidHex4 = culture.LCID.ToString("x4");
            inputMethodTip = $"{lcidHex4}:{keyboardLayout}";
        }

        int vmid = await _vms.ResolveVmIdAsync(vmName);

        onStatus?.Invoke("Starting VM for configuration...");
        JsonElement status = await _api.GetAsync<JsonElement>(
            $"{_api.VmPath(vmid)}/status/current"
        );
        if (status.GetProperty("status").GetString() != "running")
        {
            string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/start");
            await _api.PollTaskAsync(upid);
        }

        onStatus?.Invoke("Waiting for VM to get IP address...");
        string? ip = await WaitForIpAsync(vmName, TimeSpan.FromMinutes(5));
        if (ip == null)
        {
            await StopVmAsync(vmid);
            throw new TimeoutException(
                $"VM '{vmName}' did not receive an IP address within 5 minutes."
            );
        }

        _logger.LogInformation("VM {VmName} has IP {Ip}, waiting for WinRM", vmName, ip);
        onStatus?.Invoke("Waiting for WinRM...");
        await Shared.WinRmLocaleHelper.WaitForWinRmAsync(ip, TimeSpan.FromMinutes(3));

        string combinedScript = Shared.WinRmLocaleHelper.BuildCombinedPreRebootScript(
            locale,
            keyboardLayout,
            inputMethodTip,
            timezone,
            vmName,
            renameComputer,
            postCreationScript
        );

        onStatus?.Invoke("Applying configuration...");
        _logger.LogInformation("Running combined pre-reboot script on {VmName}", vmName);

        bool hasPostCreationScript = !string.IsNullOrWhiteSpace(postCreationScript);
        if (hasPostCreationScript)
        {
            string regOnlyScript = Shared.WinRmLocaleHelper.BuildCombinedPreRebootScript(
                locale,
                keyboardLayout,
                inputMethodTip,
                timezone,
                vmName,
                renameComputer,
                postCreationScript: null
            );
            if (!string.IsNullOrWhiteSpace(regOnlyScript))
                await Shared.WinRmLocaleHelper.RunWinRmCmdAsync(
                    ip,
                    username,
                    password,
                    regOnlyScript
                );
            await Shared.WinRmLocaleHelper.RunWinRmPowerShellAsync(
                ip,
                username,
                password,
                postCreationScript!
            );
        }
        else
        {
            await Shared.WinRmLocaleHelper.RunWinRmCmdAsync(ip, username, password, combinedScript);
        }

        onStatus?.Invoke("Shutting down VM...");
        await StopVmAsync(vmid);

        onStatus?.Invoke("Creating base snapshot...");
        await CreateBaseSnapshotAsync(vmName);
    }

    private async Task CreateBaseSnapshotAsync(string vmName)
    {
        int vmid = await _vms.ResolveVmIdAsync(vmName);
        string snapRaw = await _api.PostRawAsync(
            $"{_api.VmPath(vmid)}/snapshot",
            new Dictionary<string, string> { ["snapname"] = "Base" }
        );
        string snapUpid = JsonDocument.Parse(snapRaw).RootElement.GetProperty("data").GetString()!;
        await _api.PollTaskAsync(snapUpid);
    }

    private async Task StopVmAsync(int vmid)
    {
        string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/stop");
        await _api.PollTaskAsync(upid);
    }

    private async Task<string?> WaitForIpAsync(string vmName, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            string? ip = await _ipResolver.ResolveIpAsync(vmName);
            if (ip != null)
                return ip;
            await Task.Delay(3000);
        }
        return null;
    }

    private async Task<string> PostForUpidAsync(
        string path,
        Dictionary<string, string>? data = null
    )
    {
        string raw = await _api.PostRawAsync(path, data);
        return JsonDocument.Parse(raw).RootElement.GetProperty("data").GetString()!;
    }

    private static string Q(string value) => ShellRunner.Q(value);
}
