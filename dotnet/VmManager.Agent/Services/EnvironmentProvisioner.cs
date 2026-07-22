using System.Text;
using VmManager.Backends.Shared;

namespace VmManager.Agent.Services;

public sealed class EnvironmentProvisioner
{
    private const string GuestDir = @"C:\provision";
    private readonly ILogger<EnvironmentProvisioner> _logger;

    public EnvironmentProvisioner(ILogger<EnvironmentProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<ProvisionResult> RunAsync(
        string ip,
        string username,
        string password,
        EnvironmentProvisionSpec spec,
        Action<string>? log,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(spec);

        string mainScript = string.IsNullOrEmpty(spec.ScriptBase64)
            ? ""
            : Encoding.UTF8.GetString(Convert.FromBase64String(spec.ScriptBase64));

        string composite = BuildCompositeScript(spec.Files, mainScript);

        log?.Invoke($"Running provisioning script on {ip} (timeout {spec.TimeoutSeconds}s)");

        using WinRmClient client = new WinRmClient(ip, username, password);
        Task<WinRmResult> runTask = client.RunPowerShellAsync(composite);

        TimeSpan timeout = TimeSpan.FromSeconds(Math.Max(60, spec.TimeoutSeconds));
        Task completed = await Task.WhenAny(runTask, Task.Delay(timeout, ct));
        if (completed != runTask)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Provisioning script did not finish within {timeout.TotalSeconds:0}s"
            );
        }

        WinRmResult result = await runTask;
        string output = Combine(result.StdOut, result.StdErr);

        if (result.ExitCode == 0)
        {
            log?.Invoke("Provisioning script succeeded");
            return new ProvisionResult
            {
                Success = true,
                ExitCode = 0,
                Output = output,
            };
        }

        log?.Invoke($"Provisioning script failed with exit code {result.ExitCode}; gathering logs");
        string logs = await TryGatherStorkDropLogsAsync(client);
        if (!string.IsNullOrWhiteSpace(logs))
            output += "\n\n===== StorkDrop logs =====\n" + logs;

        return new ProvisionResult
        {
            Success = false,
            ExitCode = result.ExitCode,
            Output = output,
        };
    }

    private static string BuildCompositeScript(
        IReadOnlyDictionary<string, string> files,
        string mainScript
    )
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"New-Item -ItemType Directory -Force -Path '{GuestDir}' | Out-Null");

        foreach ((string relPath, string content) in files)
        {
            string safeName = Path.GetFileName(relPath);
            if (string.IsNullOrWhiteSpace(safeName))
                continue;
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content ?? ""));
            string target = GuestDir + "\\" + safeName;
            sb.AppendLine(
                $"[IO.File]::WriteAllBytes('{target}', [Convert]::FromBase64String('{b64}'))"
            );
        }

        if (!string.IsNullOrWhiteSpace(mainScript))
        {
            sb.AppendLine();
            sb.AppendLine(mainScript);
        }

        return sb.ToString();
    }

    private async Task<string> TryGatherStorkDropLogsAsync(WinRmClient client)
    {
        const string script =
            @"$out = @()
$report = Join-Path $env:TEMP 'storkdrop-apply-result.json'
if (Test-Path $report) { $out += '--- apply-result.json ---'; $out += (Get-Content $report -Raw) }
$logDir = Join-Path $env:APPDATA 'StorkDrop\Logs'
if (Test-Path $logDir) {
  $latest = Get-ChildItem $logDir -Filter *.log | Sort-Object LastWriteTime | Select-Object -Last 1
  if ($latest) { $out += '--- ' + $latest.Name + ' (tail) ---'; $out += (Get-Content $latest.FullName -Tail 200) }
}
$out -join [Environment]::NewLine";
        try
        {
            WinRmResult r = await client.RunPowerShellAsync(script);
            return r.StdOut;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gather StorkDrop logs from guest");
            return "";
        }
    }

    private static string Combine(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return stdout ?? "";
        return (stdout ?? "") + "\n[stderr]\n" + stderr;
    }
}
