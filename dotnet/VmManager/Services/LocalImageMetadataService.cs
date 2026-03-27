using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VmManager.Services;

/// <summary>
/// Reads and writes vmmanager.json metadata inside extracted image directories.
/// </summary>
public class LocalImageMetadataService : ILocalImageMetadataService
{
    private readonly ILogger<LocalImageMetadataService> _logger;

    public LocalImageMetadataService(ILogger<LocalImageMetadataService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void SaveMetadata(string extractDir, VmImageVersion version)
    {
        try
        {
            string metadataFile = Path.Combine(extractDir, "vmmanager.json");
            string json = JsonSerializer.Serialize(
                new
                {
                    name = !string.IsNullOrEmpty(version.ParentImageName)
                        ? $"{version.ParentImageName} v{version.Version}"
                        : version.Version,
                    parentImageId = version.ParentImageId,
                    parentImageName = version.ParentImageName,
                    version = version.Version,
                    feedId = version.FeedId,
                    feedUrl = version.FeedUrl,
                    feedRepository = version.FeedRepository,
                    importedAt = DateTime.UtcNow.ToString("o"),
                },
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(metadataFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save metadata to {ExtractDir}", extractDir);
        }
    }

    public LocalImageMetadata? LoadMetadata(string dir)
    {
        string metadataFile = Path.Combine(dir, "vmmanager.json");
        if (!File.Exists(metadataFile))
            return null;

        try
        {
            string json = File.ReadAllText(metadataFile);
            JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string? name = null;
            string? feedId = null,
                feedUrl = null,
                feedRepo = null;
            string? parentImageId = null,
                parentImageName = null,
                version = null;

            if (root.TryGetProperty("name", out JsonElement nameEl))
                name = nameEl.GetString();
            if (root.TryGetProperty("feedId", out JsonElement fid))
                feedId = fid.GetString();
            if (root.TryGetProperty("feedUrl", out JsonElement furl))
                feedUrl = furl.GetString();
            if (root.TryGetProperty("feedRepository", out JsonElement frepo))
                feedRepo = frepo.GetString();
            if (root.TryGetProperty("parentImageId", out JsonElement pid))
                parentImageId = pid.GetString();
            if (root.TryGetProperty("parentImageName", out JsonElement pname))
                parentImageName = pname.GetString();
            if (root.TryGetProperty("version", out JsonElement ver))
                version = ver.GetString();

            return new LocalImageMetadata(
                name ?? "",
                parentImageId,
                parentImageName,
                version,
                feedId,
                feedUrl,
                feedRepo
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load metadata from {Dir}", dir);
            return null;
        }
    }
}
