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

    private static string Esc(string value) => value.Replace("'", "''");
}
