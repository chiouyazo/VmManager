using System.Security.Cryptography;
using System.Text;

namespace VmManager.Contracts.Models;

public sealed class FeedConfiguration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public FeedType Type { get; set; } = FeedType.OCI;
    public string Url { get; set; } = "";
    public string? Repository { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Generates a deterministic ID from Type + Url + Repository.
    /// Same feed properties always produce the same ID, even if deleted and re-added.
    /// </summary>
    public void EnsureId()
    {
        if (!string.IsNullOrEmpty(Id))
            return;
        Id = ComputeId(Type, Url, Repository);
    }

    public static string ComputeId(FeedType type, string url, string? repository)
    {
        string key = $"{type}|{url?.TrimEnd('/')}|{repository ?? ""}".ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
