using System.Security.Cryptography;
using System.Text;

namespace VmManager.Services;

public static class SecureStorage
{
    public static string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return "";

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

        if (OperatingSystem.IsWindows())
        {
            byte[] encrypted = ProtectedData.Protect(
                plainBytes,
                null,
                DataProtectionScope.CurrentUser
            );
            return "DPAPI:" + Convert.ToBase64String(encrypted);
        }

        return "B64:" + Convert.ToBase64String(plainBytes);
    }

    public static string Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
            return "";

        if (protectedText.StartsWith("DPAPI:"))
        {
            if (!OperatingSystem.IsWindows())
                return "";

            byte[] encrypted = Convert.FromBase64String(protectedText[6..]);
            byte[] decrypted = ProtectedData.Unprotect(
                encrypted,
                null,
                DataProtectionScope.CurrentUser
            );
            return Encoding.UTF8.GetString(decrypted);
        }

        if (protectedText.StartsWith("B64:"))
        {
            byte[] decoded = Convert.FromBase64String(protectedText[4..]);
            return Encoding.UTF8.GetString(decoded);
        }

        return protectedText;
    }

    public static bool IsProtected(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;
        return value.StartsWith("DPAPI:") || value.StartsWith("B64:");
    }
}
