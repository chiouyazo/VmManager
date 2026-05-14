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
            await _sh.RunBashAsync($"qemu-img convert -O qcow2 {Q(vhdxPath)} {Q(diskPath)}");
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

        onStatus?.Invoke("Creating VM...");
        string networkParam = skipDefaultNetwork ? "none" : "e1000e,bridge=vmbr0";
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
            ["efidisk0"] = $"{_api.StorageId}:1,efitype=4m,pre-enrolled-keys=0,format=qcow2",
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

        onStatus?.Invoke("Importing disk image...");
        await _sh.RunBashAsync(
            $"qm importdisk {vmid} {Q(diskPath!)} {_api.StorageId} --format qcow2"
        );

        onStatus?.Invoke("Configuring VM...");
        string diskVolId = $"{_api.StorageId}:{vmid}/vm-{vmid}-disk-1.qcow2";
        await _sh.RunBashAsync($"qm set {vmid} --sata0 {diskVolId},ssd=1,discard=on");
        await _sh.RunBashAsync($"qm set {vmid} --boot order=sata0");

        onStatus?.Invoke("Creating base snapshot...");
        string snapRaw = await _api.PostRawAsync(
            $"{_api.VmPath(vmid)}/snapshot",
            new Dictionary<string, string> { ["snapname"] = "Base" }
        );
        string snapUpid = JsonDocument.Parse(snapRaw).RootElement.GetProperty("data").GetString()!;
        await _api.PollTaskAsync(snapUpid);

        onStatus?.Invoke("VM created successfully");
        _logger.LogInformation("VM created: {Name} (VMID {VmId})", finalName, vmid);
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

    private async Task StopVmAsync(int vmid)
    {
        try
        {
            string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/shutdown");
            await _api.PollTaskAsync(upid, TimeSpan.FromSeconds(30));
        }
        catch
        {
            string upid = await PostForUpidAsync($"{_api.VmPath(vmid)}/status/stop");
            await _api.PollTaskAsync(upid);
        }
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
