using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;
using VmManager.Services;

namespace VmManager.ViewModels;

public partial class ImagesViewModel : ViewModelBase
{
    private readonly ILogger<ImagesViewModel> _logger;

    private AgentClient _agentClient => App.AgentClient!;

    [ObservableProperty]
    private string _quotaText = "";

    [ObservableProperty]
    private bool _isOverQuota;

    private readonly NativeNotificationService _nativeNotifications;

    public ImagesViewModel(
        ILogger<ImagesViewModel> logger,
        NativeNotificationService nativeNotifications
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(nativeNotifications);
        _logger = logger;
        _nativeNotifications = nativeNotifications;
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
    private string _createVmStatusMessage = Resources.Status_CreatingVm;

    [ObservableProperty]
    private double _importProgress;

    [ObservableProperty]
    private string _importStatusText = "";

    [ObservableProperty]
    private string _importSpeedText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _registryNotConfigured;

    public bool ShowEmptyState => !RegistryNotConfigured && FilteredImages.Count == 0;

    public Func<string, Task<string?>>? RequestVmName { get; set; }
    public Action<string>? NavigateTo { get; set; }
    public Action<string, string>? NavigateWithMessage { get; set; }

    [ObservableProperty]
    private ObservableCollection<LocalImage> _localImages = [];

    [ObservableProperty]
    private bool _hasLocalImages;

    [ObservableProperty]
    private string? _highlightImageId;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private string _selectedSourceFilter = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private ObservableCollection<VmImage> _filteredImages = new ObservableCollection<VmImage>();

    [ObservableProperty]
    private ObservableCollection<string> _allFeatures = new ObservableCollection<string>();

    [ObservableProperty]
    private ObservableCollection<string> _activeFeatureFilters = new ObservableCollection<string>();

    [ObservableProperty]
    private ObservableCollection<string> _availableSources = new ObservableCollection<string>();

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedSourceFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    public void ToggleFeatureFilter(string feature)
    {
        if (ActiveFeatureFilters.Contains(feature))
            ActiveFeatureFilters.Remove(feature);
        else
            ActiveFeatureFilters.Add(feature);
        ApplyFilter();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        SearchQuery = "";
        SelectedSourceFilter = "All";
        ActiveFeatureFilters.Clear();
        ApplyFilter();
    }

    private void RebuildFilterOptions()
    {
        HashSet<string> features = new HashSet<string>();
        List<string> sources = new List<string> { "All" };
        HashSet<string> seenSources = new HashSet<string>();
        foreach (VmImage image in Images)
        {
            if (!string.IsNullOrEmpty(image.SourceLabel) && seenSources.Add(image.SourceLabel))
                sources.Add(image.SourceLabel);
            foreach (string feature in image.Features)
                features.Add(feature);
        }
        AllFeatures = new ObservableCollection<string>(features.OrderBy(f => f));
        AvailableSources = new ObservableCollection<string>(sources);
        SelectedSourceFilter = "All";
    }

    private void ApplyFilter()
    {
        IEnumerable<VmImage> result = Images;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string query = SearchQuery.ToLowerInvariant();
            result = result.Where(i =>
                i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || i.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || i.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || i.Features.Any(f => f.Contains(query, StringComparison.OrdinalIgnoreCase))
            );
        }

        if (SelectedSourceFilter != "All" && !string.IsNullOrEmpty(SelectedSourceFilter))
            result = result.Where(i => i.SourceLabel == SelectedSourceFilter);

        if (ActiveFeatureFilters.Count > 0)
            result = result.Where(i => ActiveFeatureFilters.All(f => i.Features.Contains(f)));

        FilteredImages = new ObservableCollection<VmImage>(result.ToList());
    }

    [RelayCommand]
    public async Task LoadCatalogAsync()
    {
        if (App.AgentClient == null)
        {
            Images = new ObservableCollection<VmImage>();
            LocalImages.Clear();
            HasLocalImages = false;
            FilteredImages = new ObservableCollection<VmImage>();
            return;
        }
        IsLoading = true;
        ShowStatus = false;
        RegistryNotConfigured = false;

        try
        {
            await LoadLocalImagesAsync();

            List<VmImage> images = new List<VmImage>();
            try
            {
                images = await _agentClient.GetCatalogAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load remote catalog, showing local images only");
            }

            if (images.Count == 0 && !HasLocalImages)
            {
                Images = new ObservableCollection<VmImage>();
                FilteredImages = new ObservableCollection<VmImage>();
                RegistryNotConfigured = true;
            }
            else
            {
                Images = new ObservableCollection<VmImage>(images);
                RebuildFilterOptions();
                ApplyFilter();
            }

            await ReconnectToActiveTasksAsync();
            await LoadQuotaAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load catalog");
            ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadQuotaAsync()
    {
        try
        {
            QuotaUsage quota = await _agentClient.GetMyQuotaAsync();
            if (quota.MaxVms > 0)
            {
                QuotaText = $"{quota.VmsOwned}/{quota.MaxVms} VMs";
                IsOverQuota = quota.VmsOwned >= quota.MaxVms;
            }
            else if (quota.GlobalMaxVms > 0)
            {
                QuotaText = $"{quota.GlobalVmCount}/{quota.GlobalMaxVms} VMs (global)";
                IsOverQuota = quota.GlobalVmCount >= quota.GlobalMaxVms;
            }
            else
            {
                QuotaText = "";
                IsOverQuota = false;
            }
        }
        catch
        {
            QuotaText = "";
            IsOverQuota = false;
        }
    }

    private async Task ReconnectToActiveTasksAsync()
    {
        try
        {
            List<AgentTaskInfo> tasks = await _agentClient.GetTasksAsync();
            foreach (AgentTaskInfo task in tasks)
            {
                if (task.IsComplete || task.IsFailed || task.IsCancelled)
                    continue;

                if (task.Title.StartsWith("Creating VM"))
                {
                    _logger.LogInformation("Reconnecting to running task: {Title}", task.Title);
                    IsCreatingVm = true;
                    _currentCreateVmTaskId = task.Id;
                    CreateVmStatusMessage = task.Status;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            (
                                TaskCompletionSource<(bool, string?)> completion,
                                CancellationTokenSource timeoutCts
                            ) = await ConnectProgressHubAsync(status =>
                            {
                                CreateVmStatusMessage = status;
                            });
                            using CancellationTokenSource __ = timeoutCts;

                            (bool success, string? error) = await completion.Task;
                            await _agentClient.DisconnectProgressHubAsync();

                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (success)
                                {
                                    NavigateWithMessage?.Invoke("MyVMs", "VM created successfully");
                                    _nativeNotifications.Show(
                                        "VM Ready",
                                        "Your VM has been created and is ready to use."
                                    );
                                }
                                else
                                {
                                    ShowError("VM creation failed: " + (error ?? "unknown"));
                                    _nativeNotifications.Show(
                                        "VM Creation Failed",
                                        error ?? "Unknown error"
                                    );
                                }
                                IsCreatingVm = false;
                                _currentCreateVmTaskId = null;
                                CreateVmStatusMessage = Resources.Status_CreatingVm;
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to reconnect to task");
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                IsCreatingVm = false;
                                _currentCreateVmTaskId = null;
                            });
                        }
                    });
                }
                else if (task.Title.StartsWith("Importing"))
                {
                    _logger.LogInformation("Reconnecting to running import: {Title}", task.Title);
                    IsImporting = true;
                    _currentImportTaskId = task.Id;
                    ImportStatusText = task.Status;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            (
                                TaskCompletionSource<(bool, string?)> completion,
                                CancellationTokenSource timeoutCts
                            ) = await ConnectProgressHubAsync(status =>
                            {
                                ImportStatusText = status;
                            });
                            using CancellationTokenSource __ = timeoutCts;

                            (bool success, string? error) = await completion.Task;
                            await _agentClient.DisconnectProgressHubAsync();

                            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                            {
                                if (success)
                                {
                                    ShowSuccess(Resources.Status_ImportComplete);
                                    _nativeNotifications.Show(
                                        "Import Complete",
                                        "Image imported and ready to use."
                                    );
                                    await LoadLocalImagesAsync();
                                    ApplyFilter();
                                }
                                else
                                {
                                    ShowError("Import failed: " + (error ?? "unknown"));
                                    _nativeNotifications.Show(
                                        "Import Failed",
                                        error ?? "Unknown error"
                                    );
                                }
                                IsImporting = false;
                                _currentImportTaskId = null;
                                ImportStatusText = "";
                                ImportProgress = 0;
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to reconnect to import task");
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                IsImporting = false;
                                _currentImportTaskId = null;
                            });
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for active tasks");
        }
    }

    [RelayCommand]
    public async Task ImportVersionAsync(VmImageVersion version)
    {
        if (IsImporting)
            return;

        IsImporting = true;
        ImportProgress = 0;
        ImportSpeedText = "";
        ImportStatusText = Resources.Status_Extracting;
        ShowStatus = false;

        try
        {
            string safeFileName = !string.IsNullOrEmpty(version.ParentImageName)
                ? version.ParentImageName + "-" + version.Version
                : SafeFileName(version.FileName);
            safeFileName = SanitizePath(safeFileName);

            (TaskCompletionSource<(bool, string?)> completion, CancellationTokenSource timeoutCts) =
                await ConnectProgressHubAsync(status =>
                {
                    ImportStatusText = status;
                });
            using CancellationTokenSource _ = timeoutCts;

            string? taskId = await _agentClient.ImportVersionAsync(
                version.FileName,
                safeFileName,
                version
            );

            if (taskId == null)
            {
                await _agentClient.DisconnectProgressHubAsync();
                ShowError("Failed to start import task");
                return;
            }

            _logger.LogInformation("Import task started: {TaskId}", taskId);
            _currentImportTaskId = taskId;
            ImportStatusText = "Starting download...";

            (bool succeeded, string? error) = await completion.Task;
            await _agentClient.DisconnectProgressHubAsync();

            if (succeeded)
            {
                ShowSuccess(Resources.Status_ImportComplete);
                version.IsLocallyAvailable = true;
                await LoadLocalImagesAsync();
                ApplyFilter();
            }
            else
            {
                ShowError("Import failed: " + (error ?? "unknown error"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import version {FileName}", version.FileName);
            ShowError(string.Format(Resources.Error_ImportFailedFormat, ex.Message));
        }
        finally
        {
            IsImporting = false;
            ImportProgress = 0;
            ImportStatusText = "";
            ImportSpeedText = "";
            _currentImportTaskId = null;
        }
    }

    private string? _currentImportTaskId;
    private string? _currentCreateVmTaskId;

    [RelayCommand]
    public async Task CancelImportAsync()
    {
        string? taskId = _currentImportTaskId;
        if (taskId == null)
            return;

        try
        {
            await _agentClient.CancelTaskAsync(taskId);
            _logger.LogInformation("Import task cancellation requested: {TaskId}", taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel import task {TaskId}", taskId);
        }
    }

    [RelayCommand]
    public async Task CancelCreateVmAsync()
    {
        string? taskId = _currentCreateVmTaskId;
        if (taskId == null)
            return;

        try
        {
            await _agentClient.CancelTaskAsync(taskId);
            _logger.LogInformation("Create VM task cancellation requested: {TaskId}", taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel create VM task {TaskId}", taskId);
        }
    }

    [RelayCommand]
    public async Task CreateVmAsync(VmImageVersion version)
    {
        AppSettings settings = await _agentClient.GetSettingsAsync();
        string safeFileName = !string.IsNullOrEmpty(version.ParentImageName)
            ? version.ParentImageName + "-" + version.Version
            : SafeFileName(version.FileName);
        safeFileName = SanitizePath(safeFileName);
        string extractDir = ExtractDir(settings.LocalVmPath, safeFileName);

        string defaultName = Path.GetFileName(extractDir);
        if (RequestVmName != null)
        {
            string? name = await RequestVmName(defaultName);
            if (name == null)
                return;
            defaultName = name;
        }

        IsCreatingVm = true;
        ShowStatus = false;

        try
        {
            VmOrigin origin = new VmOrigin
            {
                ImageId = version.ParentImageId,
                ImageName = version.ParentImageName,
                Version = version.Version,
                FeedId = version.FeedId,
                FeedUrl = version.FeedUrl,
                Repository = version.FeedRepository,
            };

            (TaskCompletionSource<(bool, string?)> completion, CancellationTokenSource timeoutCts) =
                await ConnectProgressHubAsync(status =>
                {
                    CreateVmStatusMessage = status;
                });
            using CancellationTokenSource _ = timeoutCts;

            string? taskId = await _agentClient.CreateVmAsync(
                extractDir,
                defaultName,
                settings.DefaultMemoryMb,
                settings.DefaultCpuCount,
                origin,
                version.Networks
            );

            if (taskId == null)
            {
                await _agentClient.DisconnectProgressHubAsync();
                ShowError("Failed to start VM creation task");
                return;
            }

            _logger.LogInformation("Create VM task started: {TaskId}", taskId);
            _currentCreateVmTaskId = taskId;
            CreateVmStatusMessage = "Creating VM...";

            (bool success, string? error) = await completion.Task;
            await _agentClient.DisconnectProgressHubAsync();

            if (success)
            {
                NavigateWithMessage?.Invoke(
                    "MyVMs",
                    string.Format(Resources.Status_VmCreatedFormat, defaultName)
                );
            }
            else
            {
                ShowError("VM creation failed: " + (error ?? "unknown error"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create VM {VmName} from version {FileName}",
                defaultName,
                version.FileName
            );
            ShowError(string.Format(Resources.Error_CreateVmFailedFormat, ex.Message));
        }
        finally
        {
            IsCreatingVm = false;
            _currentCreateVmTaskId = null;
            CreateVmStatusMessage = Resources.Status_CreatingVm;
        }
    }

    [RelayCommand]
    public async Task CreateVmFromLocalAsync(LocalImage localImage)
    {
        if (RequestVmName != null)
        {
            string? name = await RequestVmName(localImage.Name);
            if (name == null)
                return;
            localImage = localImage with { Name = name };
        }

        IsCreatingVm = true;
        CreateVmStatusMessage = Resources.Status_CreatingVm;
        ShowStatus = false;

        try
        {
            AppSettings settings = await _agentClient.GetSettingsAsync();

            VmOrigin? origin = null;
            if (!string.IsNullOrEmpty(localImage.FeedId))
            {
                origin = new VmOrigin
                {
                    ImageId = localImage.ParentImageId ?? "",
                    ImageName = localImage.ParentImageName ?? "",
                    Version = localImage.ImageVersion ?? "",
                    FeedId = localImage.FeedId,
                    FeedUrl = localImage.FeedUrl ?? "",
                    Repository = localImage.FeedRepository,
                };
            }

            (TaskCompletionSource<(bool, string?)> completion, CancellationTokenSource timeoutCts) =
                await ConnectProgressHubAsync(status =>
                {
                    CreateVmStatusMessage = status;
                });
            using CancellationTokenSource _ = timeoutCts;

            string? taskId = await _agentClient.CreateVmAsync(
                localImage.Path,
                localImage.Name,
                settings.DefaultMemoryMb,
                settings.DefaultCpuCount,
                origin
            );

            if (taskId == null)
            {
                await _agentClient.DisconnectProgressHubAsync();
                ShowError("Failed to start VM creation task");
                return;
            }

            CreateVmStatusMessage = "Creating VM...";

            (bool success, string? error) = await completion.Task;
            await _agentClient.DisconnectProgressHubAsync();

            if (success)
            {
                NavigateWithMessage?.Invoke(
                    "MyVMs",
                    string.Format(Resources.Status_VmCreatedFormat, localImage.Name)
                );
            }
            else
            {
                ShowError("VM creation failed: " + (error ?? "unknown error"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create VM {VmName} from local image", localImage.Name);
            ShowError(string.Format(Resources.Error_CreateVmFailedFormat, ex.Message));
        }
        finally
        {
            IsCreatingVm = false;
            _currentCreateVmTaskId = null;
            CreateVmStatusMessage = Resources.Status_CreatingVm;
        }
    }

    [RelayCommand]
    public async Task DeleteLocalImageAsync(LocalImage localImage)
    {
        try
        {
            await _agentClient.DeleteLocalImageAsync(localImage.Path);
            LocalImages.Remove(localImage);
            HasLocalImages = LocalImages.Count > 0;
            ShowSuccess(string.Format(Resources.Status_DeletedLocalImageFormat, localImage.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete local image {ImageName}", localImage.Name);
            ShowError(string.Format(Resources.Error_DeleteLocalFailedFormat, ex.Message));
        }
    }

    private async Task LoadLocalImagesAsync()
    {
        try
        {
            List<LocalImage> locals = await _agentClient.GetLocalImagesAsync();
            LocalImages = new ObservableCollection<LocalImage>(locals);
            HasLocalImages = locals.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load local images");
        }
    }

    private async Task<(
        TaskCompletionSource<(bool, string?)> Completion,
        CancellationTokenSource Timeout
    )> ConnectProgressHubAsync(Action<string>? onStatus = null)
    {
        TaskCompletionSource<(bool, string?)> completion =
            new TaskCompletionSource<(bool, string?)>();
        CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        timeoutCts.Token.Register(() => completion.TrySetResult((false, "Operation timed out")));

        await _agentClient.ConnectToProgressHubAsync(
            (_, progress, status) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (progress >= 0)
                        ImportProgress = progress * 100.0;
                    onStatus?.Invoke(status);
                });
            },
            (_, success, error) =>
            {
                completion.TrySetResult((success, error));
            },
            _ =>
            {
                completion.TrySetResult((false, "Lost connection to agent"));
                return Task.CompletedTask;
            }
        );

        return (completion, timeoutCts);
    }

    private static string SafeFileName(string fileName)
    {
        VersionReference reference = VersionReference.Parse(fileName);

        switch (reference)
        {
            case VersionReference.Local local:
                return Path.GetFileNameWithoutExtension(local.FilePath);
            case VersionReference.Nexus nexus:
                try
                {
                    string path = new Uri(nexus.DownloadUrl).AbsolutePath;
                    return Path.GetFileNameWithoutExtension(path);
                }
                catch
                {
                    return Path.GetFileNameWithoutExtension(nexus.DownloadUrl);
                }
            case VersionReference.Oci oci:
                return oci.RepositoryTag.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
            default:
                return fileName.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        }
    }

    private static string SanitizePath(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            name = name.Replace(c, '_');
        return name;
    }

    public void ResetTransientState()
    {
        IsImporting = false;
        IsCreatingVm = false;
        ImportProgress = 0;
        ImportStatusText = "";
        ImportSpeedText = "";
        if (App.AgentClient != null)
            _ = _agentClient.DisconnectProgressHubAsync();
    }

    public static string ExtractDir(string localVmPath, string fileName) =>
        localVmPath.TrimEnd('/', '\\') + "/extracted/" + fileName;
}
