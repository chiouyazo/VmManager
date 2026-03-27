using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VmManager.Backends.HyperV;

/// <summary>
/// Runs PowerShell scripts via temp files and returns stdout.
/// </summary>
public class PowerShellRunner
{
    private readonly ILogger<PowerShellRunner> _logger;

    public PowerShellRunner(ILogger<PowerShellRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Quotes a string for safe embedding in a PowerShell script.</summary>
    public static string Q(string value) => $"'{value.Replace("'", "''")}'";

    /// <summary>
    /// Writes <paramref name="script"/> to a temp .ps1 file, executes it with
    /// <c>powershell.exe -NoProfile -NonInteractive</c>, and returns trimmed stdout.
    /// Throws on non-zero exit code.
    /// </summary>
    public async Task<string> RunPsAsync(string script)
    {
        string fullScript = "$ErrorActionPreference = 'Stop'\n" + script;
        string tmp = Path.Combine(Path.GetTempPath(), $"vmm_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tmp, fullScript);
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process process = await Task.Run(() =>
                Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start PowerShell.")
            );
            using (process)
            {
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                string stdout = await stdoutTask;
                string stderr = await stderrTask;
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
            catch
            { /* Cleanup failure is non-fatal */
            }
        }
    }
}
