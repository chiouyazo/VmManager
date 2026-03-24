using System.IO;
using System.Text.Json;
using VmManager.Models;

namespace VmManager.Services;

/// <summary>
/// Reads VM image catalogs from both an OCI registry (Zot) and a local path.
/// Results from both sources are merged.
/// </summary>
public class CatalogService
{
    private readonly SettingsService _settingsService;
    private readonly OciCatalogService _ociCatalogService;
    private readonly NexusCatalogService _nexusCatalogService;

    public CatalogService(
        SettingsService settingsService,
        OciCatalogService ociCatalogService,
        NexusCatalogService nexusCatalogService
    )
    {
        _settingsService = settingsService;
        _ociCatalogService = ociCatalogService;
        _nexusCatalogService = nexusCatalogService;
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Loads images from all configured sources (local + OCI), merged.</summary>
    public async Task<List<VmImage>> LoadCatalogAsync()
    {
        AppSettings settings = _settingsService.Load();
        List<VmImage> all = new List<VmImage>();

        // Load from local catalog path (if configured)
        if (!string.IsNullOrWhiteSpace(settings.LocalCatalogPath))
        {
            try
            {
                List<VmImage> local = await LoadFromLocalAsync(settings.LocalCatalogPath);
                all.AddRange(local);
            }
            catch
            {
                // Non-fatal: local path might be unreachable
            }
        }

        // Load from OCI registry (if configured)
        if (settings.IsRegistryConfigured)
        {
            try
            {
                List<VmImage> oci = await _ociCatalogService.LoadCatalogAsync(settings);
                all.AddRange(oci);
            }
            catch
            {
                // Non-fatal: registry might be unreachable
            }
        }

        // Load from Nexus (if configured)
        if (settings.IsNexusConfigured)
        {
            try
            {
                List<VmImage> nexus = await _nexusCatalogService.LoadCatalogAsync(settings);
                all.AddRange(nexus);
            }
            catch
            {
                // Non-fatal: Nexus might be unreachable
            }
        }

        return all;
    }

    /// <summary>Alias for pages that call GetImagesAsync.</summary>
    public Task<List<VmImage>> GetImagesAsync() => LoadCatalogAsync();

    /// <summary>
    /// Returns the download path/URL for a version.
    /// Local versions use "local:{filePath}" prefix, OCI uses "repo:tag".
    /// </summary>
    public async Task<string> GetDownloadUrlAsync(string versionFileName)
    {
        // Local files are prefixed with "local:"
        if (versionFileName.StartsWith("local:"))
            return versionFileName["local:".Length..];

        // Nexus files are prefixed with "nexus:" and contain the direct download URL
        if (versionFileName.StartsWith("nexus:"))
            return versionFileName["nexus:".Length..];

        // OCI: resolve blob URL
        AppSettings settings = _settingsService.Load();
        string tag = versionFileName.Contains(':')
            ? versionFileName.Split(':').Last()
            : versionFileName;
        return await _ociCatalogService.GetBlobDownloadUrlAsync(settings, tag);
    }

    /// <summary>Returns true if the version is from a local path (not OCI/Nexus).</summary>
    public static bool IsLocalVersion(string versionFileName) =>
        versionFileName.StartsWith("local:");

    /// <summary>Returns true if the version is from Nexus.</summary>
    public static bool IsNexusVersion(string versionFileName) =>
        versionFileName.StartsWith("nexus:");

    /// <summary>Returns the auth header for OCI downloads, or null.</summary>
    public System.Net.Http.Headers.AuthenticationHeaderValue? GetAuthHeader()
    {
        AppSettings settings = _settingsService.Load();
        return OciCatalogService.BuildAuthHeader(settings);
    }

    /// <summary>Returns the auth header for Nexus downloads, or null.</summary>
    public System.Net.Http.Headers.AuthenticationHeaderValue? GetNexusAuthHeader()
    {
        AppSettings settings = _settingsService.Load();
        return NexusCatalogService.BuildAuthHeader(settings);
    }

    /// <summary>Returns true if any source is configured.</summary>
    public bool IsAnySourceConfigured()
    {
        AppSettings settings = _settingsService.Load();
        return settings.IsRegistryConfigured
            || !string.IsNullOrWhiteSpace(settings.LocalCatalogPath)
            || settings.IsNexusConfigured;
    }

    // ── Local catalog ────────────────────────────────────────────────────

    private static async Task<List<VmImage>> LoadFromLocalAsync(string catalogPath)
    {
        var catalogFile = Path.Combine(catalogPath, "catalog.json");

        var json = await Task.Run(() =>
        {
            if (!File.Exists(catalogFile))
                return null;
            return File.ReadAllText(catalogFile);
        });

        if (json == null)
            return [];

        var catalog = JsonSerializer.Deserialize<CatalogRoot>(json, JsonOptions);
        if (catalog?.Images == null)
            return [];

        // Prefix file names with "local:" so the import flow knows to copy from disk
        foreach (var img in catalog.Images)
        {
            img.Description = string.IsNullOrEmpty(img.Description)
                ? $"Local image from {catalogPath}"
                : img.Description;

            foreach (var ver in img.Versions)
            {
                var fullPath = Path.Combine(catalogPath, ver.FileName);
                ver.FileName = $"local:{fullPath}";
            }
        }

        return catalog.Images;
    }

    private class CatalogRoot
    {
        public List<VmImage> Images { get; set; } = [];
    }
}
