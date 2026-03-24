using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VmManager.Models;
using VmManager.Services;

namespace VmManager.ViewModels;

/// <summary>
/// ViewModel for the Snapshots page. Allows users to select a local VM and
/// manage its checkpoints, including pushing snapshots to the OCI registry.
/// </summary>
public partial class SnapshotsViewModel : ObservableObject
{
    private readonly HyperVService _hyperVService;
    private readonly SettingsService _settingsService;

    public SnapshotsViewModel(HyperVService hyperVService, SettingsService settingsService)
    {
        _hyperVService = hyperVService;
        _settingsService = settingsService;
    }

    [ObservableProperty]
    private ObservableCollection<VmInstance> _availableVms = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVm))]
    private VmInstance? _selectedVm;

    [ObservableProperty]
    private ObservableCollection<VmSnapshot> _snapshots = [];

    [ObservableProperty]
    private string _newSnapshotName = "";

    [ObservableProperty]
    private bool _isLoadingVms;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = "";

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isError;

    public bool HasSelectedVm => SelectedVm != null;

    [RelayCommand]
    public async Task LoadVmsAsync()
    {
        IsLoadingVms = true;
        ShowStatus = false;

        try
        {
            var vms = await _hyperVService.GetVmsAsync();
            AvailableVms = new ObservableCollection<VmInstance>(vms);

            if (SelectedVm != null)
                await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load VMs: {ex.Message}");
        }
        finally
        {
            IsLoadingVms = false;
        }
    }

    [RelayCommand]
    public async Task LoadSnapshotsAsync()
    {
        if (SelectedVm == null)
            return;

        IsBusy = true;
        ShowStatus = false;

        try
        {
            var snapshots = await _hyperVService.GetSnapshotsAsync(SelectedVm.Name);
            Snapshots = new ObservableCollection<VmSnapshot>(snapshots);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load snapshots: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CreateSnapshotAsync()
    {
        if (SelectedVm == null || string.IsNullOrWhiteSpace(NewSnapshotName))
            return;

        IsBusy = true;
        BusyText = "Creating snapshot…";

        try
        {
            await _hyperVService.CreateSnapshotAsync(SelectedVm.Name, NewSnapshotName.Trim());
            ShowSuccess($"Snapshot \"{NewSnapshotName.Trim()}\" created.");
            NewSnapshotName = "";
            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to create snapshot: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestoreSnapshotAsync(VmSnapshot snapshot)
    {
        if (SelectedVm == null)
            return;

        IsBusy = true;
        BusyText = $"Restoring \"{snapshot.Name}\"…";

        try
        {
            await _hyperVService.RestoreSnapshotAsync(SelectedVm.Name, snapshot.Id);
            ShowSuccess($"Restored to snapshot \"{snapshot.Name}\".");
            await LoadVmsAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to restore snapshot: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSnapshotAsync(VmSnapshot snapshot)
    {
        if (SelectedVm == null)
            return;

        IsBusy = true;
        BusyText = $"Deleting \"{snapshot.Name}\"…";

        try
        {
            await _hyperVService.DeleteSnapshotAsync(SelectedVm.Name, snapshot.Id);
            ShowSuccess($"Snapshot \"{snapshot.Name}\" deleted.");
            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to delete snapshot: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Set by the View to request a name for the new VM clone.</summary>
    public Func<string, Task<string?>>? RequestVmName { get; set; }

    [RelayCommand]
    public async Task CloneVmFromSnapshotAsync(VmSnapshot snapshot)
    {
        if (SelectedVm == null)
            return;

        var defaultName = $"{SelectedVm.Name}-{snapshot.Name}".Replace(" ", "-");
        var newName = RequestVmName != null ? await RequestVmName(defaultName) : defaultName;
        if (string.IsNullOrWhiteSpace(newName))
            return;

        IsBusy = true;
        BusyText = $"Cloning VM from \"{snapshot.Name}\"…";

        try
        {
            await _hyperVService.CloneVmFromSnapshotAsync(SelectedVm.Name, snapshot.Name, newName);
            ShowSuccess($"Created new VM \"{newName}\" from snapshot \"{snapshot.Name}\".");
        }
        catch (Exception ex)
        {
            ShowError($"Clone failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Exports a snapshot and pushes it to the OCI registry.
    /// The snapshot is exported to a temp directory, packaged, then pushed via the OCI API.
    /// </summary>
    [RelayCommand]
    public async Task PushSnapshotAsync(VmSnapshot snapshot)
    {
        if (SelectedVm == null)
            return;

        var settings = _settingsService.Load();
        if (!settings.IsRegistryConfigured)
        {
            ShowError("Registry is not configured. Set it in Settings first.");
            return;
        }

        IsUploading = true;
        ShowStatus = false;

        try
        {
            // Generate a tag from VM name + snapshot name (sanitized)
            var tag = $"{SelectedVm.Name}-{snapshot.Name}"
                .ToLowerInvariant()
                .Replace(' ', '-')
                .Replace(":", "-");

            await _hyperVService.PushSnapshotToRegistryAsync(
                SelectedVm.Name,
                snapshot.Id,
                settings,
                tag
            );

            ShowSuccess($"Snapshot \"{snapshot.Name}\" pushed to registry as :{tag}");
        }
        catch (Exception ex)
        {
            ShowError($"Push failed: {ex.Message}");
        }
        finally
        {
            IsUploading = false;
        }
    }

    partial void OnSelectedVmChanged(VmInstance? value)
    {
        if (value != null)
            _ = LoadSnapshotsAsync();
        else
            Snapshots.Clear();
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
