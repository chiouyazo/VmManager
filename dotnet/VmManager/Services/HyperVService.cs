using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VmManager.Models;

namespace VmManager.Services;

/// <summary>
/// Manages local Hyper-V VMs via direct WMI calls (root\virtualization\v2).
/// ~50-100x faster than PowerShell - no process spawning overhead.
/// PowerShell is kept only for ImportVmAsync, ConfigureLocaleAsync, ResetDiskAsync,
/// and PushSnapshotToRegistryAsync where WMI is impractical.
/// </summary>
public class HyperVService : IVmBackend
{
    private ManagementScope? _scope;

    private ManagementScope Scope
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

    // ── VMs ──────────────────────────────────────────────────────────────────

    public Task<List<VmInstance>> GetVmsAsync()
    {
        return Task.Run(() =>
        {
            var query = new SelectQuery("Msvm_ComputerSystem", "Caption = 'Virtual Machine'");
            using var searcher = new ManagementObjectSearcher(Scope, query);
            var vms = new List<VmInstance>();

            foreach (ManagementObject vm in searcher.Get())
            {
                using (vm)
                {
                    var state = (ushort)vm["EnabledState"];
                    var onTime = vm["OnTimeInMilliseconds"];
                    vms.Add(
                        new VmInstance
                        {
                            Name = (string)vm["ElementName"],
                            State = MapWmiState(state),
                            MemoryAssigned = state == 2 ? GetMemoryUsage(vm) : 0,
                            Uptime =
                                onTime != null
                                    ? TimeSpan.FromMilliseconds((ulong)onTime)
                                    : TimeSpan.Zero,
                        }
                    );
                }
            }
            return vms;
        });
    }

    public Task StartVmAsync(string name) => ChangeVmStateAsync(name, 2); // 2 = Enabled/Running

    public Task StopVmAsync(string name) => ChangeVmStateAsync(name, 3); // 3 = Disabled/Off (force)

    public async Task DeleteVmAsync(string name)
    {
        await Task.Run(() =>
        {
            var vm = GetVm(name) ?? throw new InvalidOperationException($"VM '{name}' not found.");

            // Force stop if running
            var state = (ushort)vm["EnabledState"];
            if (state == 2 || state == 32768) // Running or Pausing
            {
                var stopParams = vm.GetMethodParameters("RequestStateChange");
                stopParams["RequestedState"] = (ushort)3; // Off
                var stopResult = vm.InvokeMethod("RequestStateChange", stopParams, null);
                WaitForJob(stopResult);
            }

            // Destroy
            var mgmt = GetManagementService();
            var destroyParams = mgmt.GetMethodParameters("DestroySystem");
            destroyParams["AffectedSystem"] = vm.Path.Path;
            var result = mgmt.InvokeMethod("DestroySystem", destroyParams, null);
            WaitForJob(result);
        });
    }

    public Task RenameVmAsync(string currentName, string newName) =>
        Task.Run(() =>
        {
            var vm =
                GetVm(currentName)
                ?? throw new InvalidOperationException($"VM '{currentName}' not found.");
            var settings = GetVmSettings(vm);
            settings["ElementName"] = newName;

            var mgmt = GetManagementService();
            var modParams = mgmt.GetMethodParameters("ModifySystemSettings");
            modParams["SystemSettings"] = settings.GetText(TextFormat.WmiDtd20);
            var result = mgmt.InvokeMethod("ModifySystemSettings", modParams, null);
            WaitForJob(result);
        });

    public async Task<bool> ResetVmAsync(string name)
    {
        return await Task.Run(() =>
        {
            var vm = GetVm(name) ?? throw new InvalidOperationException($"VM '{name}' not found.");

            // Force stop
            var state = (ushort)vm["EnabledState"];
            if (state != 3)
            {
                var stopParams = vm.GetMethodParameters("RequestStateChange");
                stopParams["RequestedState"] = (ushort)3;
                WaitForJob(vm.InvokeMethod("RequestStateChange", stopParams, null));
            }

            // Find oldest snapshot
            var snapshots = GetSnapshotsForVm(vm);
            if (snapshots.Count == 0)
                return false;

            var oldest = snapshots.OrderBy(s => s.CreationTime).First();
            ApplySnapshotInternal(oldest.WmiPath!);
            return true;
        });
    }

    // ── Import VM (kept as PowerShell - complex VHD/VM creation) ─────────

    public async Task ImportVmAsync(
        string extractedFolder,
        string localVmPath,
        int memoryMb,
        int cpuCount,
        string? vmName = null
    )
    {
        var parentVhdx =
            Directory
                .GetFiles(extractedFolder, "*.vhdx", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(extractedFolder, "*.avhdx", SearchOption.AllDirectories))
                .FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"No .vhdx / .avhdx file found in: {extractedFolder}"
            );

        var folderName = Path.GetFileName(extractedFolder);
        var baseName = vmName;
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = System.Text.RegularExpressions.Regex.Replace(folderName, @"-\d+$", "");
            if (string.IsNullOrEmpty(baseName))
                baseName = folderName;
        }

        var memBytes = (long)memoryMb * 1024 * 1024;
        var script = $$"""
            $baseName  = {{Q(baseName)}}
            $parentVhd = {{Q(parentVhdx)}}
            $vmRoot    = {{Q(localVmPath)}}

            $taken  = @(Get-VM | Select-Object -ExpandProperty Name)
            $vmName = $baseName
            $c = 2
            while ($taken -contains $vmName) { $vmName = $baseName + '-' + $c; $c++ }

            $vmDir = Join-Path $vmRoot $vmName
            New-Item -ItemType Directory -Path $vmDir -Force | Out-Null

            $diffVhd = Join-Path $vmDir ($vmName + '.vhdx')
            New-VHD -Path $diffVhd -ParentPath $parentVhd -Differencing | Out-Null

            $vm = New-VM -Name $vmName -Generation 2 -VHDPath $diffVhd `
                         -MemoryStartupBytes {{memBytes}} `
                         -Path $vmDir

            Set-VMProcessor -VMName $vmName -Count {{cpuCount}}
            Set-VMMemory    -VMName $vmName -DynamicMemoryEnabled $false -StartupBytes {{memBytes}}

            Set-VMFirmware    -VMName $vmName -EnableSecureBoot Off
            Set-VMKeyProtector -VMName $vmName -NewLocalKeyProtector
            Enable-VMTPM       -VMName $vmName

            $sw = Get-VMSwitch -Name 'Default Switch' -ErrorAction SilentlyContinue
            if (-not $sw) { $sw = Get-VMSwitch | Select-Object -First 1 }
            if ($sw) { Add-VMNetworkAdapter -VMName $vmName -SwitchName $sw.Name }
            """;

        await RunPsAsync(script);
    }

    // ── Snapshots (WMI) ──────────────────────────────────────────────────────

    public Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName)
    {
        return Task.Run(() =>
        {
            var vm = GetVm(vmName);
            if (vm == null)
                return new List<VmSnapshot>();
            return GetSnapshotsForVm(vm)
                .Select(s => new VmSnapshot
                {
                    Id = s.Id,
                    Name = s.Name,
                    VmName = vmName,
                    CreationTime = s.CreationTime,
                })
                .ToList();
        });
    }

    public Task CreateSnapshotAsync(string vmName, string snapshotName) =>
        Task.Run(() =>
        {
            var vm =
                GetVm(vmName) ?? throw new InvalidOperationException($"VM '{vmName}' not found.");
            var snapshotService = GetSnapshotService();
            var settings = GetVmSettings(vm);

            var inParams = snapshotService.GetMethodParameters("CreateSnapshot");
            inParams["AffectedSystem"] = vm.Path.Path;
            inParams["SnapshotSettings"] = "";
            inParams["SnapshotType"] = (ushort)2; // Full snapshot

            var result = snapshotService.InvokeMethod("CreateSnapshot", inParams, null);
            WaitForJob(result);

            // Rename the snapshot (WMI creates it with a default name)
            var jobPath = (string)result["Job"];
            if (jobPath != null)
            {
                using var job = new ManagementObject(
                    new ManagementScope(@"\\.\root\virtualization\v2"),
                    new ManagementPath(jobPath),
                    null
                );
                job.Get();
                var snapshotSettingsPath = (
                    (string[])job["ResultingSnapshotSettingData"]
                )?.FirstOrDefault();
                if (snapshotSettingsPath != null)
                {
                    using var snapshotSettings = new ManagementObject(
                        new ManagementScope(@"\\.\root\virtualization\v2"),
                        new ManagementPath(snapshotSettingsPath),
                        null
                    );
                    snapshotSettings.Get();
                    snapshotSettings["ElementName"] = snapshotName;

                    var mgmt = GetManagementService();
                    var modParams = mgmt.GetMethodParameters("ModifySystemSettings");
                    modParams["SystemSettings"] = snapshotSettings.GetText(TextFormat.WmiDtd20);
                    mgmt.InvokeMethod("ModifySystemSettings", modParams, null);
                }
            }
        });

    public Task RestoreSnapshotAsync(string vmName, string snapshotId) =>
        Task.Run(() =>
        {
            var vm =
                GetVm(vmName) ?? throw new InvalidOperationException($"VM '{vmName}' not found.");

            // Force stop
            var state = (ushort)vm["EnabledState"];
            if (state != 3)
            {
                var stopParams = vm.GetMethodParameters("RequestStateChange");
                stopParams["RequestedState"] = (ushort)3;
                WaitForJob(vm.InvokeMethod("RequestStateChange", stopParams, null));
            }

            var snapshot =
                GetSnapshotsForVm(vm).FirstOrDefault(s => s.Id == snapshotId)
                ?? throw new InvalidOperationException("Snapshot not found.");
            ApplySnapshotInternal(snapshot.WmiPath!);
        });

    public Task DeleteSnapshotAsync(string vmName, string snapshotId) =>
        Task.Run(() =>
        {
            var vm =
                GetVm(vmName) ?? throw new InvalidOperationException($"VM '{vmName}' not found.");
            var snapshot =
                GetSnapshotsForVm(vm).FirstOrDefault(s => s.Id == snapshotId)
                ?? throw new InvalidOperationException("Snapshot not found.");

            var snapshotService = GetSnapshotService();
            var inParams = snapshotService.GetMethodParameters("DestroySnapshot");
            inParams["AffectedSnapshot"] = snapshot.WmiPath;
            var result = snapshotService.InvokeMethod("DestroySnapshot", inParams, null);
            WaitForJob(result);
        });

    // ── Connect ──────────────────────────────────────────────────────────────

    public Task ConnectToVmAsync(string vmName, string username = "", string password = "")
    {
        return Task.Run(() =>
        {
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                Process
                    .Start(
                        new ProcessStartInfo("cmdkey.exe")
                        {
                            Arguments =
                                $"/generic:\"TERMSRV/{vmName}\" /user:\"{username}\" /pass:\"{password}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    )
                    ?.WaitForExit();
            }

            Process.Start(
                new ProcessStartInfo("vmconnect.exe", $"localhost \"{vmName}\"")
                {
                    UseShellExecute = true,
                }
            );
        });
    }

    // ── Quick snapshot ────────────────────────────────────────────────────

    public Task QuickSnapshotAsync(string vmName)
    {
        var name = $"Snapshot {DateTime.Now:yyyy-MM-dd HH:mm}";
        return CreateSnapshotAsync(vmName, name);
    }

    // ── Reset disk (kept as PowerShell) ──────────────────────────────────

    public async Task ResetDiskAsync(string name)
    {
        var script = $$"""
            $vm = Get-VM -Name {{Q(name)}}
            $vhd = $vm | Get-VMHardDiskDrive | Select-Object -First 1
            if (-not $vhd) { throw 'No hard disk found on VM.' }
            $vhdPath = $vhd.Path
            $info = Get-VHD -Path $vhdPath
            if (-not $info.ParentPath) { throw 'Disk is not a differencing disk - cannot reset.' }
            $parentPath = $info.ParentPath
            Remove-Item $vhdPath -Force
            New-VHD -Path $vhdPath -ParentPath $parentPath -Differencing | Out-Null
            """;
        await RunPsAsync(script);
    }

    // ── Locale configuration (kept as PowerShell Direct) ─────────────────

    public async Task ConfigureLocaleAsync(string vmName, string username, string password)
    {
        var script = $$"""
            $vmName   = {{Q(vmName)}}
            $username = {{Q(username)}}
            $password = {{Q(password)}}

            $vm = Get-VM -Name $vmName
            if ($vm.State -ne 'Running') {
                Start-VM -Name $vmName
            }

            $cred    = New-Object PSCredential($username, (ConvertTo-SecureString $password -AsPlainText -Force))
            $session = $null
            $tries   = 0
            while ($tries -lt 36 -and -not $session) {
                try {
                    $session = New-PSSession -VMName $vmName -Credential $cred -ErrorAction Stop
                } catch {
                    Start-Sleep -Seconds 5
                    $tries++
                }
            }
            if (-not $session) { throw "VM '$vmName' did not become responsive within 3 minutes." }

            Invoke-Command -Session $session -ScriptBlock {
                $langList = New-WinUserLanguageList 'de-DE'
                $langList[0].InputMethodTips.Clear()
                $langList[0].InputMethodTips.Add('0407:00000407')
                Set-WinUserLanguageList $langList -Force
                Set-WinUILanguageOverride   -Language 'de-DE'
                Set-WinSystemLocale         -SystemLocale 'de-DE'
                Set-Culture                  'de-DE'
                Copy-UserInternationalSettingsToSystem -WelcomeScreen $true -NewUser $true
            }

            Remove-PSSession $session
            Stop-VM -Name $vmName -Force
            """;
        await RunPsAsync(script);
    }

    // ── Upload snapshot to network share (kept as PS) ────────────────────

    public async Task UploadSnapshotAsync(
        string vmName,
        string snapshotName,
        string snapshotId,
        string networkShareRoot
    )
    {
        var script = $$"""
            $username  = $env:USERNAME
            $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            $vmNameVal = {{Q(vmName)}}
            $dest      = Join-Path {{Q(
                networkShareRoot
            )}} "user-shares\$username\${vmNameVal}_$timestamp"
            New-Item -ItemType Directory -Path $dest -Force | Out-Null
            $cp = Get-VMCheckpoint -Id {{Q(snapshotId)}}
            if (-not $cp) { throw "Snapshot not found." }
            Export-VMCheckpoint -VMCheckpoint $cp -Path $dest
            $meta = [ordered]@{
                Username     = $username
                VmName       = $vmNameVal
                SnapshotName = {{Q(snapshotName)}}
                ExportedAt   = (Get-Date -Format 'o')
            } | ConvertTo-Json
            Set-Content -Path (Join-Path $dest 'userinfo.json') -Value $meta -Encoding UTF8
            """;
        await RunPsAsync(script);
    }

    // ── Push snapshot to OCI registry (kept as PS + HTTP) ────────────────

    private static readonly HttpClient OciHttp = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(30),
    };

    public async Task PushSnapshotToRegistryAsync(
        string vmName,
        string snapshotId,
        AppSettings settings,
        string tag
    )
    {
        var baseUrl = settings.RegistryUrl.TrimEnd('/');
        var repo = settings.RegistryRepository.Trim('/');
        var auth = OciCatalogService.BuildAuthHeader(settings);

        var tempDir = Path.Combine(Path.GetTempPath(), $"vmm_push_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await RunPsAsync(
                $$"""
                $cp = Get-VMCheckpoint -Id {{Q(snapshotId)}}
                if (-not $cp) { throw "Snapshot not found." }
                Export-VMCheckpoint -VMCheckpoint $cp -Path {{Q(tempDir)}}
                """
            );

            var tarPath = Path.Combine(tempDir, "snapshot.tar.gz");
            var exportedContent = Directory.GetDirectories(tempDir).FirstOrDefault() ?? tempDir;
            var tarExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "tar.exe"
            );
            var tarPsi = new ProcessStartInfo(tarExe)
            {
                Arguments = $"-czf \"{tarPath}\" -C \"{exportedContent}\" .",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var tarProcess =
                Process.Start(tarPsi)
                ?? throw new InvalidOperationException("Failed to start tar.exe");
            using (tarProcess)
            {
                await tarProcess.WaitForExitAsync();
                if (tarProcess.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"tar failed: {await tarProcess.StandardError.ReadToEndAsync()}"
                    );
            }

            var blobBytes = await File.ReadAllBytesAsync(tarPath);
            var digest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(blobBytes))}";

            var uploadUrl = $"{baseUrl}/v2/{repo}/blobs/uploads/";
            var initReq = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            if (auth != null)
                initReq.Headers.Authorization = auth;
            var initResp = await OciHttp.SendAsync(initReq);
            initResp.EnsureSuccessStatusCode();

            var location =
                initResp.Headers.Location?.ToString()
                ?? throw new InvalidOperationException("No upload location");
            if (!location.StartsWith("http"))
                location = $"{baseUrl}{location}";
            var sep = location.Contains('?') ? "&" : "?";

            var putReq = new HttpRequestMessage(HttpMethod.Put, $"{location}{sep}digest={digest}");
            if (auth != null)
                putReq.Headers.Authorization = auth;
            putReq.Content = new ByteArrayContent(blobBytes);
            putReq.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/octet-stream"
            );
            (await OciHttp.SendAsync(putReq)).EnsureSuccessStatusCode();

            var configBytes = "{}"u8.ToArray();
            var configDigest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(configBytes))}";

            var configInitReq = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            if (auth != null)
                configInitReq.Headers.Authorization = auth;
            var configInitResp = await OciHttp.SendAsync(configInitReq);
            configInitResp.EnsureSuccessStatusCode();
            var configLoc =
                configInitResp.Headers.Location?.ToString()
                ?? throw new InvalidOperationException("No config upload location");
            if (!configLoc.StartsWith("http"))
                configLoc = $"{baseUrl}{configLoc}";
            var configSep = configLoc.Contains('?') ? "&" : "?";

            var configPutReq = new HttpRequestMessage(
                HttpMethod.Put,
                $"{configLoc}{configSep}digest={configDigest}"
            );
            if (auth != null)
                configPutReq.Headers.Authorization = auth;
            configPutReq.Content = new ByteArrayContent(configBytes);
            configPutReq.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(
                    "application/vnd.oci.image.config.v1+json"
                );
            (await OciHttp.SendAsync(configPutReq)).EnsureSuccessStatusCode();

            var manifest = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 2,
                    mediaType = "application/vnd.oci.image.manifest.v1+json",
                    config = new
                    {
                        mediaType = "application/vnd.oci.image.config.v1+json",
                        digest = configDigest,
                        size = configBytes.Length,
                    },
                    layers = new[]
                    {
                        new
                        {
                            mediaType = "application/x-vagrant-box",
                            digest,
                            size = blobBytes.Length,
                        },
                    },
                    annotations = new Dictionary<string, string>
                    {
                        ["org.opencontainers.image.title"] = vmName,
                        ["org.opencontainers.image.description"] =
                            $"Snapshot of {vmName} pushed from VM Manager",
                        ["org.opencontainers.image.created"] = DateTime.UtcNow.ToString("o"),
                        ["org.opencontainers.image.version"] = tag,
                    },
                }
            );

            var manifestReq = new HttpRequestMessage(
                HttpMethod.Put,
                $"{baseUrl}/v2/{repo}/manifests/{tag}"
            );
            if (auth != null)
                manifestReq.Headers.Authorization = auth;
            manifestReq.Content = new StringContent(
                manifest,
                Encoding.UTF8,
                "application/vnd.oci.image.manifest.v1+json"
            );
            (await OciHttp.SendAsync(manifestReq)).EnsureSuccessStatusCode();
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    // ── WMI Helpers ──────────────────────────────────────────────────────────

    private ManagementObject? GetVm(string name)
    {
        var query = new SelectQuery(
            "Msvm_ComputerSystem",
            $"ElementName='{name.Replace("'", "''")}' AND Caption='Virtual Machine'"
        );
        using var searcher = new ManagementObjectSearcher(Scope, query);
        return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
    }

    private ManagementObject GetManagementService()
    {
        using var searcher = new ManagementObjectSearcher(
            Scope,
            new SelectQuery("Msvm_VirtualSystemManagementService")
        );
        return searcher.Get().Cast<ManagementObject>().First();
    }

    private ManagementObject GetSnapshotService()
    {
        using var searcher = new ManagementObjectSearcher(
            Scope,
            new SelectQuery("Msvm_VirtualSystemSnapshotService")
        );
        return searcher.Get().Cast<ManagementObject>().First();
    }

    private ManagementObject GetVmSettings(ManagementObject vm)
    {
        var settingsQuery = new RelatedObjectQuery(vm.Path.Path, "Msvm_VirtualSystemSettingData");
        using var settingsSearcher = new ManagementObjectSearcher(Scope, settingsQuery);
        return settingsSearcher
                .Get()
                .Cast<ManagementObject>()
                .FirstOrDefault(s =>
                    (string)s["VirtualSystemType"] == "Microsoft:Hyper-V:System:Realized"
                )
            ?? settingsSearcher.Get().Cast<ManagementObject>().First();
    }

    private long GetMemoryUsage(ManagementObject vm)
    {
        try
        {
            var memQuery = new RelatedObjectQuery(vm.Path.Path, "Msvm_MemorySettingData");
            using var memSearcher = new ManagementObjectSearcher(Scope, memQuery);
            var memObj = memSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (memObj != null)
            {
                var limit = memObj["Limit"];
                if (limit != null)
                    return (long)(ulong)limit * 1024 * 1024; // MB to bytes
            }
        }
        catch { }
        return 0;
    }

    private record SnapshotInfo(string Id, string Name, DateTime CreationTime, string? WmiPath);

    private List<SnapshotInfo> GetSnapshotsForVm(ManagementObject vm)
    {
        var snapQuery = new RelatedObjectQuery(vm.Path.Path, "Msvm_VirtualSystemSettingData");
        using var snapSearcher = new ManagementObjectSearcher(Scope, snapQuery);
        var result = new List<SnapshotInfo>();

        foreach (ManagementObject snap in snapSearcher.Get())
        {
            using (snap)
            {
                var vsType = (string)snap["VirtualSystemType"];
                if (
                    vsType != "Microsoft:Hyper-V:Snapshot:Realized"
                    && vsType != "Microsoft:Hyper-V:Snapshot:Recovery"
                )
                    continue;

                var creationTimeStr = (string)snap["CreationTime"];
                var creationTime =
                    creationTimeStr != null
                        ? ManagementDateTimeConverter.ToDateTime(creationTimeStr)
                        : DateTime.MinValue;

                result.Add(
                    new SnapshotInfo(
                        (string)snap["VirtualSystemIdentifier"],
                        (string)snap["ElementName"],
                        creationTime,
                        snap.Path.Path
                    )
                );
            }
        }

        return result;
    }

    private void ApplySnapshotInternal(string snapshotWmiPath)
    {
        var snapshotService = GetSnapshotService();
        var inParams = snapshotService.GetMethodParameters("ApplySnapshot");
        inParams["Snapshot"] = snapshotWmiPath;
        var result = snapshotService.InvokeMethod("ApplySnapshot", inParams, null);
        WaitForJob(result);
    }

    private Task ChangeVmStateAsync(string name, ushort requestedState) =>
        Task.Run(() =>
        {
            var vm = GetVm(name) ?? throw new InvalidOperationException($"VM '{name}' not found.");
            var inParams = vm.GetMethodParameters("RequestStateChange");
            inParams["RequestedState"] = requestedState;
            var result = vm.InvokeMethod("RequestStateChange", inParams, null);
            WaitForJob(result);
        });

    private static void WaitForJob(ManagementBaseObject result)
    {
        var retVal = (uint)result["ReturnValue"];
        if (retVal == 0)
            return; // Completed synchronously
        if (retVal != 4096)
            throw new InvalidOperationException(
                $"WMI operation failed with return value {retVal}."
            );

        var jobPath = (string)result["Job"];
        using var job = new ManagementObject(jobPath);
        while (true)
        {
            job.Get();
            var jobState = (ushort)job["JobState"];
            if (jobState == 7)
                return; // Completed
            if (jobState is 8 or 9 or 10) // Exception, Terminated, Killed
            {
                var error = (string?)job["ErrorDescription"] ?? "Unknown WMI job error.";
                throw new InvalidOperationException(error);
            }
            Thread.Sleep(50);
        }
    }

    private static string MapWmiState(ushort state) =>
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

    // ── PowerShell runner (only for the few operations that need it) ──────

    private static async Task<string> RunPsAsync(string script)
    {
        var fullScript = "$ErrorActionPreference = 'Stop'\n" + script;
        var tmp = Path.Combine(Path.GetTempPath(), $"vmm_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tmp, fullScript);
            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = await Task.Run(() =>
                Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start PowerShell.")
            );
            using (process)
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(stderr)
                            ? $"PowerShell exited with code {process.ExitCode}."
                            : stderr
                                .Split('\n')
                                .FirstOrDefault(l =>
                                    !l.StartsWith("At ")
                                    && !l.StartsWith("+")
                                    && !l.StartsWith("CategoryInfo")
                                    && !l.StartsWith("FullyQualifiedErrorId")
                                )
                                ?? stderr
                    );
                return stdout.Trim();
            }
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch { }
        }
    }

    private static string Q(string value) => $"'{value.Replace("'", "''")}'";
}
