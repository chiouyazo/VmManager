using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace VmManager.Services;

public class RdpSigningService
{
    private const string CertSubject = "CN=VmManager RDP Signing";
    private const string PolicyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

    private string? _thumbprint;

    public void SignRdpFile(string rdpPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string thumbprint = EnsureCertificate();
        Process process = Process.Start(
            new ProcessStartInfo("rdpsign.exe", $"/sha256 {thumbprint} \"{rdpPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        )!;
        process.WaitForExit(10000);
    }

    private string EnsureCertificate()
    {
        if (_thumbprint != null)
            return _thumbprint;

        using X509Store myStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        myStore.Open(OpenFlags.ReadOnly);
        X509Certificate2? existing = myStore.Certificates.FirstOrDefault(c =>
            c.Subject == CertSubject && c.NotAfter > DateTime.Now
        );
        myStore.Close();

        if (existing != null)
        {
            EnsureTrusted(existing);
            _thumbprint = existing.Thumbprint;
            return _thumbprint;
        }

        _thumbprint = CreateAndTrustCertificate();
        return _thumbprint;
    }

    private static string CreateAndTrustCertificate()
    {
        Process certProcess = Process.Start(
            new ProcessStartInfo(
                "powershell.exe",
                "-NoProfile -Command \""
                    + "$cert = New-SelfSignedCertificate"
                    + " -Subject '"
                    + CertSubject
                    + "'"
                    + " -CertStoreLocation 'Cert:\\CurrentUser\\My'"
                    + " -KeyUsage DigitalSignature"
                    + " -Type CodeSigningCert"
                    + " -NotAfter (Get-Date).AddYears(10);"
                    + " $cert.Thumbprint\""
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            }
        )!;
        string thumbprint = certProcess.StandardOutput.ReadToEnd().Trim();
        certProcess.WaitForExit(30000);

        using X509Store myStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        myStore.Open(OpenFlags.ReadOnly);
        X509Certificate2 cert = myStore.Certificates.First(c => c.Thumbprint == thumbprint);
        myStore.Close();

        EnsureTrusted(cert);
        return thumbprint;
    }

    private static void EnsureTrusted(X509Certificate2 cert)
    {
        AddToStore(cert, StoreName.Root);
        AddToTrustedPublisher(cert);
        AddToTrustedRdpPublishersPolicy(cert.Thumbprint);
    }

    private static void AddToStore(X509Certificate2 cert, StoreName storeName)
    {
        using X509Store store = new X509Store(storeName, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        bool alreadyPresent = store.Certificates.Any(c => c.Thumbprint == cert.Thumbprint);
        if (!alreadyPresent)
            store.Add(cert);
        store.Close();
    }

    private static void AddToTrustedPublisher(X509Certificate2 cert)
    {
        string tempCer = Path.Combine(Path.GetTempPath(), "vmmanager-rdp-signing.cer");
        try
        {
            File.WriteAllBytes(tempCer, cert.Export(X509ContentType.Cert));
            RunSilent("certutil", $"-user -addstore TrustedPublisher \"{tempCer}\"");
        }
        finally
        {
            try
            {
                File.Delete(tempCer);
            }
            catch { }
        }
    }

    private static void AddToTrustedRdpPublishersPolicy(string thumbprint)
    {
        string? existing = ReadPolicyThumbprints();
        if (existing != null && existing.Contains(thumbprint))
            return;

        string psCommand =
            "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services'"
            + " -Force | Out-Null;"
            + " Set-ItemProperty"
            + " -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services'"
            + $" -Name 'TrustedCertThumbprints' -Value '{thumbprint}' -Type String";

        Process process = Process.Start(
            new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{psCommand}\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
            }
        )!;
        process.WaitForExit(30000);
    }

    private static string? ReadPolicyThumbprints()
    {
        try
        {
            Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services"
            );
            if (key == null)
                return null;
            string? value = key.GetValue("TrustedCertThumbprints") as string;
            key.Close();
            return value;
        }
        catch
        {
            return null;
        }
    }

    private static void RunSilent(string exe, string args)
    {
        Process process = Process.Start(
            new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        )!;
        process.WaitForExit(10000);
    }
}
