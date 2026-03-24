using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VmManager.Models;

namespace VmManager.Services;

/// <summary>
/// Manages Linux containers via the Docker CLI.
/// Mirrors the IVmBackend contract so Docker containers appear alongside Hyper-V VMs.
/// </summary>
public class DockerService : IVmBackend
{
    /// <summary>Docker container state → friendly display string.</summary>
    private static string MapDockerState(string state) =>
        state.ToLowerInvariant() switch
        {
            "running" => "Running",
            "exited" => "Off",
            "created" => "Off",
            "paused" => "Paused",
            "restarting" => "Starting",
            "removing" => "Stopping",
            "dead" => "Off",
            _ => state,
        };

    // ── VMs (containers) ─────────────────────────────────────────────────────

    public async Task<List<VmInstance>> GetVmsAsync()
    {
        var json = await RunDockerAsync("ps -a --format json");
        var vms = new List<VmInstance>();

        // docker outputs one JSON object per line (not a JSON array)
        foreach (var line in json.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var container = JsonSerializer.Deserialize<DockerContainer>(line);
                if (container is null)
                    continue;

                vms.Add(
                    new VmInstance
                    {
                        Name = container.Names,
                        State = MapDockerState(container.State),
                        MemoryAssigned = 0,
                        Uptime = TimeSpan.Zero,
                        Backend = "Docker",
                    }
                );
            }
            catch
            {
                // Skip malformed lines
            }
        }

        return vms;
    }

    public async Task StartVmAsync(string name) => await RunDockerAsync($"start {Esc(name)}");

    public async Task StopVmAsync(string name) => await RunDockerAsync($"stop {Esc(name)}");

    public async Task DeleteVmAsync(string name) => await RunDockerAsync($"rm -f {Esc(name)}");

    public async Task RenameVmAsync(string currentName, string newName) =>
        await RunDockerAsync($"rename {Esc(currentName)} {Esc(newName)}");

    public async Task<bool> ResetVmAsync(string name)
    {
        // Stop + remove + reimport is not possible without knowing the original image.
        // Best-effort: stop and remove.
        await RunDockerAsync($"stop {Esc(name)}");
        await RunDockerAsync($"rm -f {Esc(name)}");
        return true;
    }

    // ── Import ───────────────────────────────────────────────────────────────

    public async Task ImportVmAsync(
        string extractedFolder,
        string localVmPath,
        int memoryMb,
        int cpuCount,
        string? vmName = null
    )
    {
        // extractedFolder is the path to a .tar image for Docker
        var name = vmName ?? Path.GetFileNameWithoutExtension(extractedFolder);

        // Load the image
        var loadOutput = await RunDockerAsync($"load -i \"{extractedFolder}\"");

        // Parse image name from "Loaded image: <name>" output
        var imageName = ParseLoadedImageName(loadOutput) ?? name;

        // Run a container from the loaded image
        await RunDockerAsync(
            $"run -d --name {Esc(name)} --memory {memoryMb}m --cpus {cpuCount} {Esc(imageName)}"
        );
    }

    // ── Snapshots (via docker commit / rmi) ──────────────────────────────────

    public Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName)
    {
        // Docker doesn't have snapshots like Hyper-V; return empty.
        return Task.FromResult(new List<VmSnapshot>());
    }

    public async Task CreateSnapshotAsync(string vmName, string snapshotName)
    {
        var tag = snapshotName.ToLowerInvariant().Replace(' ', '-');
        await RunDockerAsync($"commit {Esc(vmName)} {Esc(vmName)}:{Esc(tag)}");
    }

    public async Task RestoreSnapshotAsync(string vmName, string snapshotId)
    {
        // snapshotId is the image tag (vmName:tag)
        await RunDockerAsync($"stop {Esc(vmName)}");
        await RunDockerAsync($"rm -f {Esc(vmName)}");
        await RunDockerAsync($"run -d --name {Esc(vmName)} {Esc(snapshotId)}");
    }

    public async Task DeleteSnapshotAsync(string vmName, string snapshotId)
    {
        // snapshotId is the image reference to remove
        await RunDockerAsync($"rmi {Esc(snapshotId)}");
    }

    // ── Connect ──────────────────────────────────────────────────────────────

    public Task ConnectToVmAsync(string vmName, string username = "", string password = "")
    {
        // Open a terminal with docker exec
        var psi = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c start cmd /k docker exec -it {Esc(vmName)} bash",
            UseShellExecute = true,
        };
        Process.Start(psi);
        return Task.CompletedTask;
    }

    // ── Docker CLI runner ────────────────────────────────────────────────────

    private static async Task<string> RunDockerAsync(string arguments)
    {
        var psi = new ProcessStartInfo("docker")
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start docker.");

        using (process)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"docker exited with code {process.ExitCode}"
                        : stderr.Trim()
                );

            return stdout.Trim();
        }
    }

    private static string? ParseLoadedImageName(string loadOutput)
    {
        // Output looks like: "Loaded image: myimage:latest"
        foreach (var line in loadOutput.Split('\n'))
        {
            if (line.StartsWith("Loaded image:", StringComparison.OrdinalIgnoreCase))
                return line["Loaded image:".Length..].Trim();
        }

        return null;
    }

    /// <summary>Escapes a value for safe shell usage.</summary>
    private static string Esc(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    // ── JSON model for docker ps --format json ───────────────────────────────

    private class DockerContainer
    {
        public string Names { get; set; } = "";
        public string State { get; set; } = "";
        public string Image { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
