using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VmManager.Models;
using VmManager.Services;

namespace VmManager.ViewModels;

/// <summary>
/// ViewModel for the My VMs page. Lists local Hyper-V VMs and exposes
/// power-management, connection, rename and reset commands.
/// </summary>
public partial class MyVmsViewModel : ObservableObject
{
    private readonly HyperVService _hyperVService;
    private readonly DockerService _dockerService;
    private readonly VmBackendFactory _backendFactory;
    private readonly SettingsService _settingsService;
    private readonly PreflightService _preflightService;

    public MyVmsViewModel(
        HyperVService hyperVService,
        DockerService dockerService,
        VmBackendFactory backendFactory,
        SettingsService settingsService,
        PreflightService preflightService
    )
    {
        _hyperVService = hyperVService;
        _dockerService = dockerService;
        _backendFactory = backendFactory;
        _settingsService = settingsService;
        _preflightService = preflightService;
    }

    [ObservableProperty]
    private ObservableCollection<VmInstance> _vms = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _hyperVUnavailable;

    /// <summary>
    /// Set by the View to show confirmation dialogs.
    /// Returns true if user confirms, false to cancel.
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmAction { get; set; }

    /// <summary>
    /// Set by the View to request a name for new VMs.
    /// Returns the name or null to cancel.
    /// </summary>
    public Func<string, Task<string?>>? RequestVmName { get; set; }

    /// <summary>Set by the View to navigate to a different page.</summary>
    public Action<string>? NavigateTo { get; set; }

    // ── Credentials ──────────────────────────────────────────────────────────

    public string CredentialTooltip
    {
        get
        {
            var s = _settingsService.Load();
            return $"User: {s.DefaultVmUsername}\nPassword: {s.DefaultVmPassword}";
        }
    }

    public string DefaultUsername => _settingsService.Load().DefaultVmUsername;
    public string DefaultPassword => _settingsService.Load().DefaultVmPassword;

    // ── Managed VMs tracking ─────────────────────────────────────────────────

    private static readonly string ManagedVmsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VmManager",
        "managed-vms.json"
    );

    private HashSet<string> _managedVms = [];

    private void LoadManagedVms()
    {
        try
        {
            if (File.Exists(ManagedVmsPath))
            {
                var json = File.ReadAllText(ManagedVmsPath);
                _managedVms = JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
            }
        }
        catch
        {
            _managedVms = [];
        }
    }

    public static void TrackManagedVm(string vmName)
    {
        try
        {
            var path = ManagedVmsPath;
            HashSet<string> vms = [];
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                vms = JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
            }

            vms.Add(vmName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(vms, new JsonSerializerOptions { WriteIndented = true })
            );
        }
        catch
        { /* non-fatal */
        }
    }

    // ── Notes persistence ────────────────────────────────────────────────────

    private static readonly string NotesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VmManager",
        "vm-notes.json"
    );

    private Dictionary<string, string> _notes = [];

    private void LoadNotes()
    {
        try
        {
            if (File.Exists(NotesPath))
            {
                var json = File.ReadAllText(NotesPath);
                _notes = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            }
        }
        catch
        {
            _notes = [];
        }
    }

    private void SaveNotes()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
            File.WriteAllText(
                NotesPath,
                JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = true })
            );
        }
        catch
        { /* non-fatal */
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ShowStatus = false;
        HyperVUnavailable = false;

        try
        {
            LoadNotes();
            LoadManagedVms();
            var allVms = new List<VmInstance>();

            // Load Hyper-V VMs
            try
            {
                var hyperVVms = await _hyperVService.GetVmsAsync();
                allVms.AddRange(hyperVVms);
            }
            catch (Exception ex)
            {
                if (
                    ex.Message.Contains("Hyper-V")
                    || ex.Message.Contains("Get-VM")
                    || ex.Message.Contains("not recognized")
                    || ex.Message.Contains("cannot be loaded")
                    || ex.Message.Contains("virtualization")
                )
                    HyperVUnavailable = true;
                else
                    ShowError(ex.Message);
            }

            // Load Docker containers
            try
            {
                var dockerVms = await _dockerService.GetVmsAsync();
                allVms.AddRange(dockerVms);
            }
            catch
            {
                // Docker not available - silently skip
            }

            // Attach notes and managed status to VMs
            foreach (var vm in allVms)
            {
                if (_notes.TryGetValue(vm.Name, out var note))
                    vm.Notes = note;
                if (vm.Backend == "HyperV")
                    vm.IsManaged = _managedVms.Contains(vm.Name);
                else
                    vm.IsManaged = true; // Docker containers are always app-managed
            }

            Vms = new ObservableCollection<VmInstance>(allVms);
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

    /// <summary>Resolves the correct backend for a VM based on its Backend property.</summary>
    private IVmBackend BackendFor(VmInstance vm) => _backendFactory.GetBackendByName(vm.Backend);

    [RelayCommand]
    public async Task StartVmAsync(VmInstance vm)
    {
        if (vm.Backend == "HyperV")
        {
            IsBusy = true;
            ShowStatus = false;
            StatusMessage = $"Checking available RAM for {vm.Name}…";

            try
            {
                var ramError = await _preflightService.CheckRamForVmAsync(vm.Name);
                if (ramError != null)
                {
                    ShowError(ramError);
                    return;
                }
            }
            catch
            { /* proceed anyway */
            }
            finally
            {
                IsBusy = false;
            }
        }

        await RunOperationAsync(
            $"Starting {vm.Name}…",
            () => BackendFor(vm).StartVmAsync(vm.Name),
            $"{vm.Name} started."
        );
    }

    [RelayCommand]
    public async Task StopVmAsync(VmInstance vm) =>
        await RunOperationAsync(
            $"Stopping {vm.Name}…",
            () => BackendFor(vm).StopVmAsync(vm.Name),
            $"{vm.Name} stopped."
        );

    [RelayCommand]
    public async Task DeleteVmAsync(VmInstance vm)
    {
        if (ConfirmAction != null)
        {
            var typeLabel = vm.Backend == "Docker" ? "container" : "VM";
            var confirmed = await ConfirmAction(
                $"Delete \"{vm.Name}\"?",
                $"This will permanently remove the {typeLabel}."
            );
            if (!confirmed)
                return;
        }

        await RunOperationAsync(
            $"Deleting {vm.Name}…",
            () => BackendFor(vm).DeleteVmAsync(vm.Name),
            $"{vm.Name} deleted."
        );

        // Clean up notes
        _notes.Remove(vm.Name);
        SaveNotes();
    }

    [RelayCommand]
    public async Task ConnectVmAsync(VmInstance vm)
    {
        try
        {
            var s = _settingsService.Load();
            await BackendFor(vm)
                .ConnectToVmAsync(vm.Name, s.DefaultVmUsername, s.DefaultVmPassword);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to connect: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task RenameVmAsync(VmInstance vm)
    {
        if (string.IsNullOrWhiteSpace(vm.PendingRename) || vm.PendingRename == vm.Name)
            return;

        // Move notes to new name
        var oldName = vm.Name;
        await RunOperationAsync(
            $"Renaming {vm.Name}…",
            () => BackendFor(vm).RenameVmAsync(vm.Name, vm.PendingRename),
            $"Renamed to {vm.PendingRename}."
        );

        if (_notes.TryGetValue(oldName, out var note))
        {
            _notes.Remove(oldName);
            _notes[vm.PendingRename] = note;
            SaveNotes();
        }
    }

    [RelayCommand]
    public async Task QuickSnapshotAsync(VmInstance vm)
    {
        if (vm.Backend == "HyperV")
        {
            await RunOperationAsync(
                $"Creating snapshot of {vm.Name}…",
                () => _hyperVService.QuickSnapshotAsync(vm.Name),
                $"Snapshot of {vm.Name} created."
            );
        }
        else
        {
            var snapshotName = $"snapshot-{DateTime.Now:yyyyMMdd-HHmm}";
            await RunOperationAsync(
                $"Committing {vm.Name}…",
                () => BackendFor(vm).CreateSnapshotAsync(vm.Name, snapshotName),
                $"Snapshot of {vm.Name} committed."
            );
        }
    }

    [RelayCommand]
    public async Task ResetVmAsync(VmInstance vm)
    {
        if (ConfirmAction != null)
        {
            var confirmed = await ConfirmAction(
                $"Reset \"{vm.Name}\"?",
                vm.Backend == "Docker"
                    ? "This will stop and remove the container."
                    : "This will stop the VM and restore it to its oldest snapshot.\n"
                        + "If no snapshots exist, the VM's disk will be reset to the original base image."
            );
            if (!confirmed)
                return;
        }

        IsBusy = true;
        ShowStatus = false;
        StatusMessage = $"Resetting {vm.Name}…";

        try
        {
            var backend = BackendFor(vm);
            var restored = await backend.ResetVmAsync(vm.Name);
            if (restored)
            {
                ShowSuccess($"{vm.Name} reset.");
            }
            else if (vm.Backend == "HyperV")
            {
                // No checkpoints - reset the differencing disk to original state
                await _hyperVService.ResetDiskAsync(vm.Name);
                ShowSuccess($"{vm.Name} reset to original base image.");
            }
            else
            {
                ShowSuccess($"{vm.Name} removed.");
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void SaveNoteForVm(VmInstance vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Notes))
            _notes.Remove(vm.Name);
        else
            _notes[vm.Name] = vm.Notes;
        SaveNotes();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task RunOperationAsync(
        string busyMessage,
        Func<Task> operation,
        string successMessage
    )
    {
        IsBusy = true;
        ShowStatus = false;
        StatusMessage = busyMessage;

        try
        {
            await operation();
            ShowSuccess(successMessage);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
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
