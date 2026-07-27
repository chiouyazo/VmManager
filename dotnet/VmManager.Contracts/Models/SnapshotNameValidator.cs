using System.Text;
using System.Text.RegularExpressions;

namespace VmManager.Contracts.Models;

/// <summary>
/// Proxmox snapshot names must match <c>^[A-Za-z][A-Za-z0-9_-]+$</c> (start with a
/// letter, then only letters/digits/hyphen/underscore, no spaces or punctuation).
/// Passing anything else makes the Proxmox API reject it with
/// "Parameter verification failed" (HTTP 400).
/// </summary>
public static class SnapshotNameValidator
{
    private const int MaxLength = 40;

    private static readonly Regex ValidPattern = new Regex(
        @"^[A-Za-z][A-Za-z0-9_\-]{0,39}$",
        RegexOptions.Compiled
    );

    public static bool IsValid(string name) =>
        !string.IsNullOrWhiteSpace(name) && ValidPattern.IsMatch(name);

    public static string? GetError(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Snapshot name cannot be empty.";
        if (!char.IsLetter(name[0]))
            return "Snapshot name must start with a letter.";
        if (name.Length > MaxLength)
            return $"Snapshot name cannot be longer than {MaxLength} characters.";
        if (!ValidPattern.IsMatch(name))
            return "Snapshot name can only contain letters, numbers, hyphens and underscores (no spaces).";
        return null;
    }

    /// <summary>
    /// Converts free-text into a valid Proxmox snapshot name: spaces and other
    /// invalid characters become underscores (collapsed), the name is trimmed,
    /// forced to start with a letter, and capped at 40 characters.
    /// Returns null if nothing usable remains (e.g. digits/symbols only).
    /// </summary>
    public static string? Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        StringBuilder sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            bool ok =
                (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '-'
                || c == '_';
            sb.Append(ok ? c : '_');
        }

        string result = sb.ToString();
        while (result.Contains("__"))
            result = result.Replace("__", "_");
        result = result.Trim('_', '-');

        // Must start with a letter: drop any leading digits/hyphens/underscores.
        int start = 0;
        while (start < result.Length && !char.IsLetter(result[start]))
            start++;
        result = start < result.Length ? result[start..] : "";

        if (string.IsNullOrEmpty(result))
            return null;

        if (result.Length > MaxLength)
            result = result[..MaxLength].Trim('_', '-');

        return string.IsNullOrEmpty(result) ? null : result;
    }
}
