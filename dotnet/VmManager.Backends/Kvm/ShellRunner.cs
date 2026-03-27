using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VmManager.Backends.Kvm;

public class ShellRunner
{
    private readonly ILogger<ShellRunner> _logger;

    public ShellRunner(ILogger<ShellRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public static string Q(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public async Task<string> RunBashAsync(string script)
    {
        string preview = script.Length > 200 ? script[..200] + "..." : script;
        _logger.LogDebug("Running bash: {Script}", preview);

        string fullScript = "#!/bin/bash\nset -e\n" + script;
        string tmp = Path.Combine(Path.GetTempPath(), $"vmm_{Guid.NewGuid():N}.sh");
        try
        {
            await File.WriteAllTextAsync(tmp, fullScript);
            ProcessStartInfo psi = new ProcessStartInfo("bash")
            {
                Arguments = tmp,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process process = await Task.Run(() =>
                Process.Start(psi) ?? throw new InvalidOperationException("Failed to start bash.")
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
                            ? $"Bash exited with code {process.ExitCode}."
                            : stderr.Trim()
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
}
