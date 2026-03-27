namespace VmManager.Contracts.Models;

/// <summary>
/// Discriminated union representing the source reference for a VM image version.
/// Replaces scattered string prefix parsing ("local:", "nexus:") with a type-safe approach.
/// </summary>
public abstract record VersionReference
{
    private VersionReference() { }

    public sealed record Local(string FilePath) : VersionReference;

    public sealed record Nexus(string DownloadUrl) : VersionReference;

    public sealed record Oci(string RepositoryTag) : VersionReference;

    public static VersionReference Parse(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.StartsWith("local:", StringComparison.Ordinal))
            return new Local(encoded["local:".Length..]);
        if (encoded.StartsWith("nexus:", StringComparison.Ordinal))
            return new Nexus(encoded["nexus:".Length..]);
        return new Oci(encoded);
    }

    public string Encode() =>
        this switch
        {
            Local l => $"local:{l.FilePath}",
            Nexus n => $"nexus:{n.DownloadUrl}",
            Oci o => o.RepositoryTag,
            _ => throw new InvalidOperationException(
                $"Unknown VersionReference type: {GetType().Name}"
            ),
        };

    public bool IsLocal => this is Local;
    public bool IsNexus => this is Nexus;
    public bool IsOci => this is Oci;
}
