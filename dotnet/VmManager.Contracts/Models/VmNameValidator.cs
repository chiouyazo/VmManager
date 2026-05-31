using System.Text.RegularExpressions;

namespace VmManager.Contracts.Models;

public static class VmNameValidator
{
    private static readonly Regex ValidPattern = new Regex(
        @"^[a-zA-Z0-9][a-zA-Z0-9\-\.]{0,62}$",
        RegexOptions.Compiled
    );

    public static bool IsValid(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return ValidPattern.IsMatch(name);
    }

    public static string? GetError(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "VM name cannot be empty.";

        if (name.Length > 63)
            return "VM name cannot be longer than 63 characters.";

        if (!char.IsLetterOrDigit(name[0]))
            return "VM name must start with a letter or number.";

        if (!ValidPattern.IsMatch(name))
            return "VM name can only contain letters, numbers, hyphens, and dots.";

        return null;
    }
}
