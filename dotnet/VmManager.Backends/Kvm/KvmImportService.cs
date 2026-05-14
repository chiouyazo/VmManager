using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Interfaces;

namespace VmManager.Backends.Kvm;

public class KvmImportService
{
    private readonly ShellRunner _sh;
    private readonly ILogger<KvmImportService> _logger;
    private readonly IVmIpResolver _ipResolver;

    public KvmImportService(
        ShellRunner sh,
        ILogger<KvmImportService> logger,
        IVmIpResolver ipResolver
    )
    {
        ArgumentNullException.ThrowIfNull(sh);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(ipResolver);
        _sh = sh;
        _logger = logger;
        _ipResolver = ipResolver;
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
            "Importing VM {VmName} from {Folder} (Memory={MemoryMb}MB, CPU={CpuCount})",
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

        string folderName = Path.GetFileName(extractedFolder);
        string baseName = vmName ?? Regex.Replace(folderName, @"-\d+$", "");
        if (string.IsNullOrEmpty(baseName))
            baseName = folderName;

        string existingVms = await _sh.RunBashAsync(
            "virsh list --all --name 2>/dev/null | grep -v '^$' || true"
        );
        HashSet<string> taken = new(
            existingVms.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );

        string finalName = baseName;
        int counter = 2;
        while (taken.Contains(finalName))
        {
            finalName = baseName + "-" + counter;
            counter++;
        }

        string vmDir = Path.Combine(localVmPath, finalName);
        Directory.CreateDirectory(vmDir);
        string qcow2Path = Path.Combine(vmDir, finalName + ".qcow2");

        if (vhdxPath != null)
        {
            onStatus?.Invoke("Converting VHDX to QCOW2...");
            _logger.LogInformation("Converting to qcow2: {Source} -> {Dest}", vhdxPath, qcow2Path);
            await _sh.RunBashAsync($"qemu-img convert -O qcow2 {Q(vhdxPath)} {Q(qcow2Path)}");
        }
        else
        {
            long totalBytes = new FileInfo(diskPath!).Length;
            long totalMb = totalBytes / (1024 * 1024);
            _logger.LogInformation(
                "Copying qcow2 ({SizeMb} MB): {Source} -> {Dest}",
                totalMb,
                diskPath,
                qcow2Path
            );
            await CopyWithProgressAsync(
                diskPath!,
                qcow2Path,
                totalBytes,
                onStatus,
                cancellationToken
            );
        }

        string networkArg = skipDefaultNetwork
            ? "--network none"
            : "--network network=default,model=e1000e";

        onStatus?.Invoke("Creating VM...");
        _logger.LogInformation("Running virt-install for {VmName}", finalName);
        string script = $"""
            virt-install --name {Q(finalName)} \
                --memory {memoryMb} \
                --vcpus {cpuCount},sockets=1,cores={cpuCount},threads=1 \
                --disk path={Q(qcow2Path)},bus=sata,cache=writeback,io=threads,discard=unmap \
                --import \
                --os-variant win10 \
                --boot uefi,firmware.feature0.name=secure-boot,firmware.feature0.enabled=no \
                --cpu host-passthrough \
                --noautoconsole \
                --noreboot \
                {networkArg} \
                --tpm emulator,model=tpm-crb,version=2.0 \
                --features hyperv.relaxed.state=on,hyperv.vapic.state=on,hyperv.spinlocks.state=on,hyperv.spinlocks.retries=8191 \
                --channel unix,target_type=virtio,name=org.qemu.guest_agent.0
            """;

        await _sh.RunBashAsync(script);
        _logger.LogInformation("virt-install completed for {VmName}", finalName);

        onStatus?.Invoke("Preparing UEFI firmware...");
        await PrepareNvramQcow2Async(finalName);

        onStatus?.Invoke("VM created successfully");
        _logger.LogInformation("VM created successfully: {VmName}", finalName);
    }

    private async Task PrepareNvramQcow2Async(string vmName)
    {
        try
        {
            string xmlOutput = await _sh.RunBashAsync($"virsh dumpxml {Q(vmName)}");

            string? nvramPath = null;
            string? templatePath = null;
            foreach (string line in xmlOutput.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("<nvram"))
                {
                    int tplIdx = trimmed.IndexOf("template='");
                    if (tplIdx >= 0)
                    {
                        int tplStart = tplIdx + "template='".Length;
                        int tplEnd = trimmed.IndexOf('\'', tplStart);
                        if (tplEnd > tplStart)
                            templatePath = trimmed[tplStart..tplEnd];
                    }
                    int start = trimmed.IndexOf('>') + 1;
                    int end = trimmed.IndexOf("</nvram>");
                    if (start > 0 && end > start)
                        nvramPath = trimmed[start..end];
                }
            }

            if (nvramPath == null)
            {
                _logger.LogWarning("Could not find NVRAM path in VM {VmName} XML", vmName);
                return;
            }

            string sourceForConvert =
                File.Exists(nvramPath) ? nvramPath
                : (templatePath != null && File.Exists(templatePath)) ? templatePath
                : "/usr/share/OVMF/OVMF_VARS_4M.fd";

            string qcow2Path = Path.ChangeExtension(nvramPath, ".qcow2");
            _logger.LogInformation(
                "Creating qcow2 NVRAM from {Source} -> {Dest}",
                sourceForConvert,
                qcow2Path
            );

            await _sh.RunBashAsync(
                $"qemu-img convert -f raw -O qcow2 {Q(sourceForConvert)} {Q(qcow2Path)}"
            );

            string escapedOld = nvramPath.Replace("/", "\\/");
            string escapedNew = qcow2Path.Replace("/", "\\/");
            await _sh.RunBashAsync(
                $"virsh dumpxml {Q(vmName)} > /tmp/vmm-nvram-fix.xml && "
                    + $"sed -i \"s|format='raw'>{escapedOld}|format='qcow2'>{escapedNew}|\" /tmp/vmm-nvram-fix.xml && "
                    + $"virsh define /tmp/vmm-nvram-fix.xml && "
                    + $"rm -f /tmp/vmm-nvram-fix.xml"
            );

            if (File.Exists(nvramPath))
                File.Delete(nvramPath);

            _logger.LogInformation("NVRAM prepared as qcow2 for VM {VmName}", vmName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to prepare qcow2 NVRAM for VM {VmName}. Snapshots may not work.",
                vmName
            );
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
        string state = await GetVmStateAsync(vmName);
        if (state != "running")
        {
            _logger.LogInformation("Starting VM {VmName} for locale configuration", vmName);
            await _sh.RunBashAsync($"virsh start {Q(vmName)}");
        }

        onStatus?.Invoke("Waiting for VM to get IP address...");
        string? ip = await WaitForIpAsync(vmName, TimeSpan.FromMinutes(5));
        if (ip == null)
        {
            await ForceStopVmAsync(vmName);
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
        _logger.LogInformation("Locale applied to VM {VmName}, rebooting", vmName);

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
            string reapplyScript = Shared.WinRmLocaleHelper.BuildReapplyScript(
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
                    reapplyScript
                );
                _logger.LogInformation("Keyboard re-applied after reboot for VM {VmName}", vmName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to re-apply keyboard after reboot for VM {VmName}",
                    vmName
                );
            }
        }

        onStatus?.Invoke("Shutting down VM...");
        _logger.LogInformation("Shutting down VM {VmName} after locale configuration", vmName);
        await ForceStopVmAsync(vmName);
    }

    public async Task RunPostCreationViaWinRmAsync(
        string vmName,
        string username,
        string password,
        bool renameComputer,
        string? postCreationScript,
        Action<string>? onStatus = null
    )
    {
        string state = await GetVmStateAsync(vmName);
        if (state != "running")
        {
            await _sh.RunBashAsync($"virsh start {Q(vmName)}");
        }

        onStatus?.Invoke("Waiting for VM to get IP address...");
        string? ip = await WaitForIpAsync(vmName, TimeSpan.FromMinutes(5));
        if (ip == null)
        {
            await ForceStopVmAsync(vmName);
            throw new TimeoutException($"VM '{vmName}' did not receive an IP within 5 minutes.");
        }

        try
        {
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

        await ForceStopVmAsync(vmName);
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

    private async Task ForceStopVmAsync(string vmName)
    {
        await _sh.RunBashAsync($"virsh shutdown {Q(vmName)} 2>/dev/null || true");
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            string state = await GetVmStateAsync(vmName);
            if (state == "shut off")
                return;
        }
        await _sh.RunBashAsync($"virsh destroy {Q(vmName)} 2>/dev/null || true");
    }

    private async Task<string> GetVmStateAsync(string vmName)
    {
        try
        {
            string info = await _sh.RunBashAsync($"virsh dominfo {Q(vmName)}");
            foreach (string line in info.Split('\n'))
            {
                if (line.StartsWith("State:", StringComparison.OrdinalIgnoreCase))
                    return line[6..].Trim();
            }
        }
        catch { }
        return "unknown";
    }

    private static async Task CopyWithProgressAsync(
        string source,
        string dest,
        long totalBytes,
        Action<string>? onStatus,
        CancellationToken ct = default
    )
    {
        byte[] buffer = new byte[4 * 1024 * 1024]; // 4MB buffer
        long copied = 0;
        DateTime lastReport = DateTime.MinValue;

        using FileStream src = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            buffer.Length,
            true
        );
        using FileStream dst = new(
            dest,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            true
        );

        int bytesRead;
        while ((bytesRead = await src.ReadAsync(buffer, ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            await dst.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            copied += bytesRead;

            if ((DateTime.UtcNow - lastReport).TotalSeconds >= 1)
            {
                int pct = (int)(copied * 100 / totalBytes);
                long copiedMb = copied / (1024 * 1024);
                long totalMb = totalBytes / (1024 * 1024);
                onStatus?.Invoke($"Copying disk image... {pct}% ({copiedMb}/{totalMb} MB)");
                lastReport = DateTime.UtcNow;
            }
        }
    }

    private static string Q(string value) => ShellRunner.Q(value);
}
