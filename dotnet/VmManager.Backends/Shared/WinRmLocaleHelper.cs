using System.Globalization;
using System.Linq;

namespace VmManager.Backends.Shared;

public static class WinRmLocaleHelper
{
    public static string BuildLocaleScript(
        string locale,
        string keyboardLayout,
        string inputMethodTip,
        string timezone
    )
    {
        string tzLine = string.IsNullOrWhiteSpace(timezone)
            ? ""
            : $"Set-TimeZone -Id '{Esc(timezone)}'";

        return @$"
$langList = New-WinUserLanguageList '{Esc(locale)}'
$langList[0].InputMethodTips.Clear()
$langList[0].InputMethodTips.Add('{Esc(inputMethodTip)}')
Set-WinUserLanguageList $langList -Force
Set-WinDefaultInputMethodOverride -InputTip '{Esc(inputMethodTip)}' -ErrorAction SilentlyContinue
Set-WinUILanguageOverride -Language '{Esc(locale)}' -ErrorAction SilentlyContinue
reg load 'HKU\TempDefault' 'C:\Users\Default\NTUSER.DAT' 2>$null
$lcid = [System.Globalization.CultureInfo]::new('{Esc(locale)}').LCID
$lcidHex = '{{0:x8}}' -f $lcid
reg add 'HKU\TempDefault\Keyboard Layout\Preload' /v 1 /t REG_SZ /d '{Esc(keyboardLayout)}' /f 2>$null
reg delete 'HKU\TempDefault\Keyboard Layout\Preload' /v 2 /f 2>$null
reg add 'HKU\TempDefault\Control Panel\International' /v Locale /t REG_SZ /d $lcidHex /f 2>$null
reg add 'HKU\TempDefault\Control Panel\International' /v LocaleName /t REG_SZ /d '{Esc(locale)}' /f 2>$null
reg unload 'HKU\TempDefault' 2>$null
$curSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
reg add ""HKU\$curSid\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d 0 /f 2>$null
reg add ""HKU\$curSid\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Hidden /t REG_DWORD /d 1 /f 2>$null
Set-WinSystemLocale -SystemLocale '{Esc(locale)}'
Copy-UserInternationalSettingsToSystem -WelcomeScreen $true -NewUser $true
{tzLine}
";
    }

    public static string BuildReapplyScript(
        string locale,
        string keyboardLayout,
        string inputMethodTip
    )
    {
        return @$"
$langList = New-WinUserLanguageList '{Esc(locale)}'
$langList[0].InputMethodTips.Clear()
$langList[0].InputMethodTips.Add('{Esc(inputMethodTip)}')
Set-WinUserLanguageList $langList -Force
Set-WinDefaultInputMethodOverride -InputTip '{Esc(inputMethodTip)}' -ErrorAction SilentlyContinue
Set-WinUILanguageOverride -Language '{Esc(locale)}' -ErrorAction SilentlyContinue
Copy-UserInternationalSettingsToSystem -WelcomeScreen $true -NewUser $true
";
    }

    public static async Task RunWinRmPowerShellAsync(
        string ip,
        string username,
        string password,
        string psScript
    )
    {
        using WinRmClient client = new WinRmClient(ip, username, password);
        WinRmResult result = await client.RunPowerShellAsync(psScript);
        if (result.ExitCode != 0)
        {
            string error = !string.IsNullOrWhiteSpace(result.StdErr)
                ? result.StdErr
                : $"PowerShell exited with code {result.ExitCode}";
            throw new InvalidOperationException($"WinRM command failed: {error}");
        }
    }

    public static async Task RunPostCreationAsync(
        string ip,
        string username,
        string password,
        string vmName,
        bool renameComputer,
        string? postCreationScript
    )
    {
        if (renameComputer)
        {
            string computerName = ToWindowsComputerName(vmName);
            string renameScript = $"Rename-Computer -NewName '{Esc(computerName)}' -Force";
            await RunWinRmPowerShellAsync(ip, username, password, renameScript);
        }

        if (!string.IsNullOrWhiteSpace(postCreationScript))
            await RunWinRmPowerShellAsync(ip, username, password, postCreationScript);

        if (renameComputer || !string.IsNullOrWhiteSpace(postCreationScript))
            await RunWinRmPowerShellAsync(ip, username, password, "Restart-Computer -Force");
    }

    public static string BuildCombinedPreRebootScript(
        string? locale,
        string? keyboardLayout,
        string? inputMethodTip,
        string? timezone,
        string? vmName,
        bool renameComputer,
        string? postCreationScript
    )
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(locale) && !string.IsNullOrWhiteSpace(keyboardLayout))
        {
            string lcidHex8 = CultureInfo.GetCultureInfo(locale!).LCID.ToString("x8");
            string tzLine = string.IsNullOrWhiteSpace(timezone) ? "" : $"tzutil /s \"{timezone!}\"";

            parts.Add(
                $@"reg add ""HKCU\Keyboard Layout\Preload"" /v 1 /t REG_SZ /d ""{Esc(keyboardLayout!)}"" /f >nul 2>&1"
                    + $@" & reg delete ""HKCU\Keyboard Layout\Preload"" /v 2 /f >nul 2>&1"
                    + $@" & reg add ""HKCU\Control Panel\International"" /v Locale /t REG_SZ /d ""{lcidHex8}"" /f >nul 2>&1"
                    + $@" & reg add ""HKCU\Control Panel\International"" /v LocaleName /t REG_SZ /d ""{Esc(locale!)}"" /f >nul 2>&1"
                    + $@" & reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d 0 /f >nul 2>&1"
                    + $@" & reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Hidden /t REG_DWORD /d 1 /f >nul 2>&1"
                    + $@" & reg load HKU\TempDefault C:\Users\Default\NTUSER.DAT >nul 2>&1"
                    + $@" & reg add ""HKU\TempDefault\Keyboard Layout\Preload"" /v 1 /t REG_SZ /d ""{Esc(keyboardLayout!)}"" /f >nul 2>&1"
                    + $@" & reg delete ""HKU\TempDefault\Keyboard Layout\Preload"" /v 2 /f >nul 2>&1"
                    + $@" & reg add ""HKU\TempDefault\Control Panel\International"" /v Locale /t REG_SZ /d ""{lcidHex8}"" /f >nul 2>&1"
                    + $@" & reg add ""HKU\TempDefault\Control Panel\International"" /v LocaleName /t REG_SZ /d ""{Esc(locale!)}"" /f >nul 2>&1"
                    + $@" & reg unload HKU\TempDefault >nul 2>&1"
                    + (string.IsNullOrWhiteSpace(tzLine) ? "" : $" & {tzLine}")
            );
        }

        if (renameComputer && !string.IsNullOrWhiteSpace(vmName))
        {
            parts.Add(
                $@"reg add ""HKLM\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName"" /v ComputerName /t REG_SZ /d ""{Esc(vmName!)}"" /f >nul 2>&1"
                    + $@" & reg add ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" /v Hostname /t REG_SZ /d ""{Esc(vmName!)}"" /f >nul 2>&1"
                    + $@" & reg add ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" /v ""NV Hostname"" /t REG_SZ /d ""{Esc(vmName!)}"" /f >nul 2>&1"
            );
        }

        if (!string.IsNullOrWhiteSpace(postCreationScript))
            parts.Add(postCreationScript!);

        return string.Join(" & ", parts);
    }

    public static async Task RunWinRmCmdAsync(
        string ip,
        string username,
        string password,
        string cmdScript
    )
    {
        using WinRmClient client = new WinRmClient(ip, username, password);
        WinRmResult result = await client.RunCmdAsync(cmdScript);
        if (result.ExitCode != 0)
        {
            string error = !string.IsNullOrWhiteSpace(result.StdErr)
                ? result.StdErr
                : $"cmd exited with code {result.ExitCode}";
            throw new InvalidOperationException($"WinRM cmd failed: {error}");
        }
    }

    public static async Task WaitForWinRmAsync(string ip, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var connectTask = tcp.ConnectAsync(ip, 5985);
                if (
                    await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask
                    && tcp.Connected
                )
                    return;
            }
            catch { }
            await Task.Delay(2000);
        }
        throw new TimeoutException(
            $"WinRM on {ip} did not become available within {timeout.TotalSeconds}s"
        );
    }

    /// <summary>
    /// Converts a VM name into a valid Windows (NetBIOS) computer name:
    /// ASCII letters/digits/hyphens only (everything else becomes a hyphen),
    /// no leading/trailing/repeated hyphens, max 15 characters, never all-numeric.
    /// Windows rejects names containing '.', spaces or other punctuation, which
    /// otherwise makes Rename-Computer fail with "The parameter is incorrect".
    /// </summary>
    public static string ToWindowsComputerName(string vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName))
            return "WIN";

        System.Text.StringBuilder sb = new System.Text.StringBuilder(vmName.Length);
        foreach (char c in vmName)
        {
            bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            sb.Append(ok ? c : '-');
        }

        string name = sb.ToString();
        while (name.Contains("--"))
            name = name.Replace("--", "-");
        name = name.Trim('-');

        if (name.Length > 15)
            name = name[..15].Trim('-');

        if (string.IsNullOrEmpty(name))
            return "WIN";

        if (name.All(char.IsDigit))
        {
            name = "PC-" + name;
            if (name.Length > 15)
                name = name[..15].Trim('-');
        }

        return name;
    }

    private static string Esc(string value) => value.Replace("'", "''");
}
