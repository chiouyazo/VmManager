using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VmManager.Models;
using VmManager.Services;

namespace VmManager.ViewModels;

/// <summary>
/// ViewModel for the Images (marketplace) page.
///
/// Two-step workflow:
///   1. Import - downloads the .box from the OCI registry and extracts it locally (one-time per version).
///   2. Create - registers the already-extracted files with Hyper-V (repeatable, fast).
/// </summary>
public partial class ImagesViewModel : ObservableObject
{
    private readonly CatalogService _catalogService;
    private readonly HyperVService _hyperVService;
    private readonly ImportService _importService;
    private readonly SettingsService _settingsService;
    private readonly PreflightService _preflightService;

    public ImagesViewModel(
        CatalogService catalogService,
        HyperVService hyperVService,
        ImportService importService,
        SettingsService settingsService,
        PreflightService preflightService
    )
    {
        _catalogService = catalogService;
        _hyperVService = hyperVService;
        _importService = importService;
        _settingsService = settingsService;
        _preflightService = preflightService;
    }

    [ObservableProperty]
    private ObservableCollection<VmImage> _images = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private bool _isCreatingVm;

    [ObservableProperty]
    private string _createVmStatusMessage = "Creating VM…";

    [ObservableProperty]
    private double _importProgress;

    [ObservableProperty]
    private string _importStatusText = "";

    [ObservableProperty]
    private string _importSpeedText = "";

    private CancellationTokenSource? _importCts;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _registryNotConfigured;

    /// <summary>Set by the View to request a VM name. Returns null to cancel.</summary>
    public Func<string, Task<string?>>? RequestVmName { get; set; }

    /// <summary>Set by the View to navigate to a page after VM creation.</summary>
    public Action<string>? NavigateTo { get; set; }

    [ObservableProperty]
    private ObservableCollection<LocalImage> _localImages = [];

    [ObservableProperty]
    private bool _hasLocalImages;

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadCatalogAsync()
    {
        IsLoading = true;
        ShowStatus = false;
        RegistryNotConfigured = false;

        try
        {
            var settings = _settingsService.Load();

            // Always scan local images regardless of source config
            await ScanLocalImagesAsync(settings);

            if (!_catalogService.IsAnySourceConfigured())
            {
                RegistryNotConfigured = true;
                return;
            }

            var images = await _catalogService.LoadCatalogAsync();

            // Check which versions are already extracted locally
            foreach (var img in images)
            foreach (var ver in img.Versions)
            {
                var safeName = SafeFileName(ver.FileName);
                ver.IsLocallyAvailable = IsExtracted(settings.LocalVmPath, safeName);
            }

            Images = new ObservableCollection<VmImage>(images);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Downloads and extracts a version from the OCI registry to local disk.
    /// </summary>
    [RelayCommand]
    public async Task ImportVersionAsync(VmImageVersion version)
    {
        if (IsImporting)
            return;

        var settings = _settingsService.Load();

        // Pre-flight: check disk space
        var diskError = await _preflightService.CheckDiskSpaceAsync(
            settings.LocalVmPath,
            version.SizeGb
        );
        if (diskError != null)
        {
            ShowError(diskError);
            return;
        }

        var safeFileName = SafeFileName(version.FileName);
        var localBoxPath = Path.Combine(settings.LocalVmPath, "downloads", safeFileName + ".box");
        var extractDir = ExtractDir(settings.LocalVmPath, safeFileName);
        var isLocal = CatalogService.IsLocalVersion(version.FileName);

        _importCts = new CancellationTokenSource();
        IsImporting = true;
        ImportProgress = 0;
        ImportSpeedText = "";
        ShowStatus = false;

        try
        {
            // Step 1 - get .box file locally
            if (isLocal)
            {
                // Copy from local/network path
                var sourcePath = await _catalogService.GetDownloadUrlAsync(version.FileName);
                ImportStatusText = $"Copying {version.Version}…";
                await _importService.CopyWithProgressAsync(
                    sourcePath,
                    localBoxPath,
                    new Progress<double>(p => ImportProgress = p * 0.5),
                    _importCts.Token
                );
            }
            else
            {
                // Download from OCI registry
                ImportStatusText = $"Downloading {version.Version}…";
                var downloadUrl = await _catalogService.GetDownloadUrlAsync(version.FileName);
                var auth = _catalogService.GetAuthHeader();
                await _importService.DownloadWithProgressAsync(
                    downloadUrl,
                    localBoxPath,
                    new Progress<Models.DownloadProgress>(p =>
                    {
                        ImportProgress = p.Percent * 0.5;
                        var speedMb = p.SpeedBytesPerSec / 1024.0 / 1024.0;
                        var dlMb = p.DownloadedBytes / 1024.0 / 1024.0;
                        var totalMb = p.TotalBytes / 1024.0 / 1024.0;
                        ImportSpeedText = p.Eta.HasValue
                            ? $"{dlMb:F0}/{totalMb:F0} MB - {speedMb:F1} MB/s - ~{p.Eta.Value:mm\\:ss} remaining"
                            : $"{dlMb:F0} MB - {speedMb:F1} MB/s";
                    }),
                    _importCts.Token,
                    auth
                );
            }

            // Step 2 - extract the .box archive
            ImportStatusText = "Extracting archive…";
            ImportSpeedText = "";
            await _importService.ExtractAsync(
                localBoxPath,
                extractDir,
                new Progress<double>(p => ImportProgress = 50 + p * 0.5),
                _importCts.Token
            );

            // Clean up downloaded archive
            try
            {
                File.Delete(localBoxPath);
            }
            catch
            { /* non-fatal */
            }

            version.IsLocallyAvailable = true;
            ShowSuccess($"v{version.Version} is ready - click Create VM to spin up an instance.");
        }
        catch (OperationCanceledException)
        {
            ShowError("Import cancelled.");
        }
        catch (Exception ex)
        {
            ShowError($"Import failed: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
            ImportStatusText = "";
            ImportSpeedText = "";
            _importCts?.Dispose();
            _importCts = null;
        }
    }

    /// <summary>Cancels the current import/download.</summary>
    [RelayCommand]
    public void CancelImport()
    {
        _importCts?.Cancel();
    }

    /// <summary>
    /// Registers a locally available version with Hyper-V as a new VM.
    /// </summary>
    [RelayCommand]
    public async Task CreateVmAsync(VmImageVersion version)
    {
        var settings = _settingsService.Load();
        var safeFileName = SafeFileName(version.FileName);
        var extractDir = ExtractDir(settings.LocalVmPath, safeFileName);

        if (!Directory.Exists(extractDir))
        {
            ShowError("Local files not found. Please Import the version first.");
            return;
        }

        // Ask for VM name
        var defaultName = Path.GetFileName(extractDir);
        if (RequestVmName != null)
        {
            var name = await RequestVmName(defaultName);
            if (name == null)
                return; // Cancelled
            defaultName = name;
        }

        IsCreatingVm = true;
        ShowStatus = false;

        try
        {
            CreateVmStatusMessage = "Creating VM…";
            await _hyperVService.ImportVmAsync(
                extractDir,
                settings.LocalVmPath,
                settings.DefaultMemoryMb,
                settings.DefaultCpuCount,
                defaultName
            );

            CreateVmStatusMessage = "Applying DE locale + QWERTZ keyboard (VM is booting)…";
            await _hyperVService.ConfigureLocaleAsync(
                defaultName,
                settings.DefaultVmUsername,
                settings.DefaultVmPassword
            );

            ShowSuccess($"VM \"{defaultName}\" created with DE locale and QWERTZ keyboard.");
            NavigateTo?.Invoke("MyVMs");
        }
        catch (Exception ex)
        {
            ShowError($"Create VM failed: {ex.Message}");
        }
        finally
        {
            IsCreatingVm = false;
            CreateVmStatusMessage = "Creating VM…";
        }
    }

    /// <summary>Creates a VM from a local extracted image.</summary>
    [RelayCommand]
    public async Task CreateVmFromLocalAsync(LocalImage localImage)
    {
        if (!Directory.Exists(localImage.Path))
        {
            ShowError("Local image folder not found.");
            return;
        }

        // Ask for VM name
        if (RequestVmName != null)
        {
            var name = await RequestVmName(localImage.Name);
            if (name == null)
                return;
            localImage = localImage with { Name = name };
        }

        var settings = _settingsService.Load();
        IsCreatingVm = true;
        ShowStatus = false;

        try
        {
            await _hyperVService.ImportVmAsync(
                localImage.Path,
                settings.LocalVmPath,
                settings.DefaultMemoryMb,
                settings.DefaultCpuCount,
                localImage.Name
            );
            ShowSuccess($"VM \"{localImage.Name}\" created.");
            NavigateTo?.Invoke("MyVMs");
        }
        catch (Exception ex)
        {
            ShowError($"Create VM failed: {ex.Message}");
        }
        finally
        {
            IsCreatingVm = false;
        }
    }

    /// <summary>Deletes a locally extracted image from disk.</summary>
    [RelayCommand]
    public async Task DeleteLocalImageAsync(LocalImage localImage)
    {
        try
        {
            await Task.Run(() => Directory.Delete(localImage.Path, true));
            LocalImages.Remove(localImage);
            HasLocalImages = LocalImages.Count > 0;
            ShowSuccess($"Deleted local image \"{localImage.Name}\".");
        }
        catch (Exception ex)
        {
            ShowError($"Failed to delete: {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task ScanLocalImagesAsync(AppSettings settings)
    {
        return Task.Run(() =>
        {
            var extractedDir = Path.Combine(settings.LocalVmPath, "extracted");
            if (!Directory.Exists(extractedDir))
                return;

            var locals = new List<LocalImage>();
            foreach (var dir in Directory.GetDirectories(extractedDir))
            {
                // Only include directories that have a .vhdx (valid extracted image)
                var vhdxFiles = Directory.GetFiles(dir, "*.vhdx", SearchOption.AllDirectories);
                if (vhdxFiles.Length == 0)
                    continue;

                var dirInfo = new DirectoryInfo(dir);
                var totalSize = vhdxFiles.Sum(f => new FileInfo(f).Length);

                locals.Add(
                    new LocalImage
                    {
                        Name = dirInfo.Name,
                        Path = dir,
                        SizeGb = totalSize / 1024.0 / 1024.0 / 1024.0,
                        ExtractedAt = dirInfo.CreationTime,
                    }
                );
            }

            App.Current.Dispatcher.Invoke(() =>
            {
                LocalImages = new ObservableCollection<LocalImage>(
                    locals.OrderByDescending(l => l.ExtractedAt)
                );
                HasLocalImages = locals.Count > 0;
            });
        });
    }

    /// <summary>Sanitizes a version FileName for filesystem use (strips prefixes, replaces invalid chars).</summary>
    private static string SafeFileName(string fileName)
    {
        // Strip "local:" prefix if present
        if (fileName.StartsWith("local:"))
            fileName = Path.GetFileNameWithoutExtension(fileName["local:".Length..]);
        return fileName.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
    }

    public static string ExtractDir(string localVmPath, string fileName) =>
        Path.Combine(localVmPath, "extracted", Path.GetFileNameWithoutExtension(fileName));

    private static bool IsExtracted(string localVmPath, string fileName)
    {
        var dir = ExtractDir(localVmPath, fileName);
        return Directory.Exists(dir)
            && Directory.GetFiles(dir, "*.vhdx", SearchOption.AllDirectories).Length > 0;
    }

    private void ShowSuccess(string message)
    {
        IsError = false;
        StatusMessage = message;
        ShowStatus = true;
    }

    private void ShowError(string message)
    {
        IsError = true;
        StatusMessage = message;
        ShowStatus = true;
    }
}
