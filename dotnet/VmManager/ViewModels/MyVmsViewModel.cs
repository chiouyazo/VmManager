using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VmManager.Services;

namespace VmManager.ViewModels;

public partial class MyVmsViewModel : ViewModelBase
{
    private readonly ILogger<MyVmsViewModel> _logger;
    private readonly PermissionService _permissionService;

    private AgentClient _agentClient => App.AgentClient!;

    public MyVmsViewModel(PermissionService permissionService, ILogger<MyVmsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(permissionService);
        ArgumentNullException.ThrowIfNull(logger);
        _permissionService = permissionService;
        _logger = logger;
    }

    public ObservableCollection<VmInstanceViewModel> SharedVms { get; } = [];

    [ObservableProperty]
    private ObservableCollection<VmInstanceViewModel> _vms = [];

    public ObservableCollection<VmGroup> GroupedVms { get; } = new ObservableCollection<VmGroup>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _backendUnavailable;

    [ObservableProperty]
    private bool _noVmsDetected;

    [ObservableProperty]
    private string _troubleshootReport = "";

    [ObservableProperty]
    private bool _showTroubleshoot;

    public Func<string, string, Task<bool>>? ConfirmAction { get; set; }
    public Func<string, Task<string?>>? RequestVmName { get; set; }
    public Func<List<string>, Task<int>>? RequestPushFeed { get; set; }
    public Func<List<string>, Task<int>>? RequestPushRepository { get; set; }
    public Action<string>? NavigateTo { get; set; }
    public Action<string>? NavigateToMarketplaceImage { get; set; }

    public bool IsAdmin => _permissionService.IsAdmin;

    [ObservableProperty]
    private string _credentialTooltip = "";

    [ObservableProperty]
    private string _defaultUsername = "";

    [ObservableProperty]
    private string _defaultPassword = "";

    [ObservableProperty]
    private bool _isPushing;

    [ObservableProperty]
    private double _pushProgress;

    [ObservableProperty]
    private string _pushStatusText = "";

    [ObservableProperty]
    private string _pushSpeedText = "";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (App.AgentClient == null)
        {
            Vms.Clear();
            GroupedVms.Clear();
            return;
        }
        IsLoading = true;
        ShowStatus = false;
        BackendUnavailable = false;
        NoVmsDetected = false;
        ShowTroubleshoot = false;

        try
        {
            AppSettings settings = await _agentClient.GetSettingsAsync();
            DefaultUsername = settings.DefaultVmUsername;
            DefaultPassword = settings.DefaultVmPassword;

            List<VmInstance> allVms = await _agentClient.GetVmsAsync();

            HashSet<string> seen = new HashSet<string>();
            foreach (VmInstance vmData in allVms)
            {
                seen.Add(vmData.Name);
                VmInstanceViewModel? existing = Vms.FirstOrDefault(v => v.Name == vmData.Name);
                if (existing != null)
                {
                    existing.UpdateData(vmData);
                }
                else
                {
                    Vms.Add(new VmInstanceViewModel(vmData, _permissionService));
                }
            }

            for (int i = Vms.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(Vms[i].Name))
                    Vms.RemoveAt(i);
            }
            List<VmInstanceViewModel> wrapped = Vms.ToList();
            RebuildGroups(wrapped);

            SharedVms.Clear();
            foreach (VmInstanceViewModel vm in wrapped.Where(v => v.IsSharedWithCurrentUser))
                SharedVms.Add(vm);

            NoVmsDetected = allVms.Count == 0;

            _ = LoadAllSnapshotCountsAsync(wrapped);

            foreach (VmInstanceViewModel vm in wrapped)
            {
                if (vm.Data.IsRunning && !vm.IsBusy && vm.IsManaged)
                    _ = CheckRdpReadinessAsync(vm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh VMs");
            if (
                ex.Message.Contains("Hyper-V")
                || ex.Message.Contains("libvirt")
                || ex.Message.Contains("not recognized")
            )
                BackendUnavailable = true;
            else
                ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RunTroubleshootAsync()
    {
        ShowTroubleshoot = false;
        TroubleshootReport = Resources.Status_RunningDiagnostics;
        ShowTroubleshoot = true;

        try
        {
            TroubleshootReport = await _agentClient.TroubleshootAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Troubleshoot failed");
            TroubleshootReport = "Troubleshoot failed: " + ex.Message;
        }
    }

    [RelayCommand]
    public void StartVm(VmInstanceViewModel vm)
    {
        if (vm.IsBusy)
            return;
        _ = StartVmBackgroundAsync(vm);
    }

    private async Task StartVmBackgroundAsync(VmInstanceViewModel vm)
    {
        vm.IsBusy = true;
        vm.SetStateOverride("Starting");
        vm.StatusMessage = "Starting...";
        try
        {
            await _agentClient.StartVmAsync(vm.Name);
            vm.StatusMessage = "Booting...";
            await PollVmStateAsync(vm, "Running", TimeSpan.FromMinutes(3));
            if (vm.IsManaged)
            {
                vm.StatusMessage = "Waiting for RDP...";
                await PollRdpReadyAsync(vm, TimeSpan.FromMinutes(5));
            }
            vm.SetStateOverride(null);
            await RefreshAsync();
            ShowSuccess(string.Format(Resources.Status_StartedFormat, vm.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start VM {VmName}", vm.Name);
            ShowError(ex.Message);
        }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
            vm.SetStateOverride(null);
        }
    }

    [RelayCommand]
    public void StopVm(VmInstanceViewModel vm)
    {
        if (vm.IsBusy)
            return;
        _ = StopVmBackgroundAsync(vm);
    }

    private async Task StopVmBackgroundAsync(VmInstanceViewModel vm)
    {
        vm.IsBusy = true;
        vm.SetStateOverride("Stopping");
        vm.StatusMessage = "Stopping...";
        try
        {
            await _agentClient.StopVmAsync(vm.Name);
            vm.StatusMessage = "Shutting down...";
            await PollVmStateAsync(vm, "Off", TimeSpan.FromMinutes(2));
            vm.SetStateOverride(null);
            await RefreshAsync();
            ShowSuccess(string.Format(Resources.Status_StoppedFormat, vm.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop VM {VmName}", vm.Name);
            ShowError(ex.Message);
        }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
            vm.SetStateOverride(null);
        }
    }

    [RelayCommand]
    public async Task DeleteVmAsync(VmInstanceViewModel vm)
    {
        if (vm.IsBusy)
            return;
        if (ConfirmAction != null)
        {
            bool confirmed = await ConfirmAction(
                string.Format(Resources.Confirm_DeleteTitleFormat, vm.Name),
                string.Format(Resources.Confirm_DeleteVmFormat, Resources.Confirm_DeleteVmType)
            );
            if (!confirmed)
                return;
        }

        _ = DeleteVmBackgroundAsync(vm);
    }

    private async Task DeleteVmBackgroundAsync(VmInstanceViewModel vm)
    {
        vm.IsBusy = true;
        vm.StatusMessage = "Deleting...";
        try
        {
            await _agentClient.DeleteVmAsync(vm.Name);
            await RefreshAsync();
            ShowSuccess(string.Format(Resources.Status_DeletedFormat, vm.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete VM {VmName}", vm.Name);
            ShowError(ex.Message);
        }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
        }
    }

    [RelayCommand]
    public async Task ConnectVmAsync(VmInstanceViewModel vm)
    {
        try
        {
            await _agentClient.ConnectToVmAsync(vm.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to VM {VmName}", vm.Name);
            ShowError(string.Format(Resources.Error_ConnectFailedFormat, ex.Message));
        }
    }

    [RelayCommand]
    public void RenameVm(VmInstanceViewModel vm)
    {
        if (vm.IsBusy || string.IsNullOrWhiteSpace(vm.PendingRename) || vm.PendingRename == vm.Name)
            return;
        _ = RenameVmBackgroundAsync(vm);
    }

    private async Task RenameVmBackgroundAsync(VmInstanceViewModel vm)
    {
        vm.IsBusy = true;
        vm.StatusMessage = "Renaming...";
        try
        {
            await _agentClient.RenameVmAsync(vm.Name, vm.PendingRename);
            await RefreshAsync();
            ShowSuccess(string.Format(Resources.Status_RenamedFormat, vm.PendingRename));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename VM {VmName}", vm.Name);
            ShowError(ex.Message);
        }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
        }
    }

    [RelayCommand]
    public void GoToOriginImage(VmInstanceViewModel vm)
    {
        if (vm.Origin != null && !string.IsNullOrEmpty(vm.Origin.ImageId))
            NavigateToMarketplaceImage?.Invoke(vm.Origin.ImageId);
    }

    [RelayCommand]
    public async Task LoadSnapshotsForVmAsync(VmInstanceViewModel vm)
    {
        if (vm.Backend is not "HyperV" and not "KVM" and not "Proxmox")
            return;

        try
        {
            List<VmSnapshot> snapshots = await _agentClient.GetSnapshotsAsync(vm.Name);
            vm.Snapshots.Clear();
            foreach (VmSnapshot snapshot in snapshots)
                vm.Snapshots.Add(snapshot);
            vm.SnapshotCount = snapshots.Count;
            vm.SnapshotsLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load snapshots for VM {VmName}", vm.Name);
            ShowError(string.Format(Resources.Error_SnapshotsFailedFormat, ex.Message));
        }
    }

    [RelayCommand]
    public async Task CreateSnapshotForVmAsync(VmInstanceViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.NewSnapshotName))
            return;

        if (string.Equals(vm.NewSnapshotName.Trim(), "Base", StringComparison.OrdinalIgnoreCase))
        {
            ShowError(Resources.Error_BaseNameReserved);
            return;
        }

        vm.IsBusy = true;
        vm.StatusMessage = "Creating snapshot...";

        try
        {
            await _agentClient.CreateSnapshotAsync(vm.Name, vm.NewSnapshotName.Trim());
            ShowSuccess(
                string.Format(Resources.Status_SnapshotCreatedFormat, vm.NewSnapshotName.Trim())
            );
            vm.NewSnapshotName = "";
            await LoadSnapshotsForVmAsync(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot for VM {VmName}", vm.Name);
            ShowError(string.Format(Resources.Error_SnapshotFailedFormat, ex.Message));
        }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
        }
    }

    [RelayCommand]
    public async Task RestoreSnapshotAsync(VmSnapshot snapshot)
    {
        if (ConfirmAction != null)
        {
            bool confirmed = await ConfirmAction(
                string.Format(Resources.Confirm_RestoreTitleFormat, snapshot.Name),
                Resources.Confirm_RestoreMessage
            );
            if (!confirmed)
                return;
        }

        VmInstanceViewModel? vm = Vms.FirstOrDefault(v => v.Name == snapshot.VmName);
        if (vm != null)
        {
            vm.IsBusy = true;
            vm.StatusMessage = "Restoring snapshot...";
        }

        try
        {
            await _agentClient.RestoreSnapshotAsync(snapshot.VmName, snapshot.Id);
            await RefreshAsync();
            ShowSuccess(string.Format(Resources.Status_RestoredFormat, snapshot.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restore snapshot {SnapshotName} for VM {VmName}",
                snapshot.Name,
                snapshot.VmName
            );
            ShowError(string.Format(Resources.Error_RestoreFailedFormat, ex.Message));
        }
        finally
        {
            if (vm != null)
            {
                vm.IsBusy = false;
                vm.StatusMessage = "";
            }
        }
    }

    [RelayCommand]
    public async Task ResetToBaseAsync(VmInstanceViewModel vm)
    {
        if (vm.IsBusy)
            return;
        if (ConfirmAction != null)
        {
            bool confirmed = await ConfirmAction(
                string.Format(Resources.Confirm_ResetTitleFormat, vm.Name),
                Resources.Confirm_ResetMessage
            );
            if (!confirmed)
                return;
        }
        _ = ResetToBaseBackgroundAsync(vm);
    }

    private async Task ResetToBaseBackgroundAsync(VmInstanceViewModel vm)
    {
        vm.IsBusy = true;
        vm.StatusMessage = "Resetting to base...";
        try
        {
            await _agentClient.ResetVmAsync(vm.Name);
            await RefreshAsync();
            ShowSuccess(string.Format(Resources.Status_ResetCompleteFormat, vm.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset VM {VmName}", vm.Name);
            ShowError(ex.Message);
        }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
        }
    }

    [RelayCommand]
    public void ApplyLocale(VmInstanceViewModel vm)
    {
        if (vm.IsBusy)
            return;
        _ = ApplyLocaleBackgroundAsync(vm);
    }

    private async Task ApplyLocaleBackgroundAsync(VmInstanceViewModel vm)
    {
        vm.IsBusy = true;
        vm.StatusMessage = "Applying locale...";

        try
        {
            (TaskCompletionSource<(bool, string?)> completion, CancellationTokenSource timeoutCts) =
                await ConnectProgressHubAsync(status =>
                {
                    vm.StatusMessage = status;
                });
            using CancellationTokenSource _ = timeoutCts;

            string? taskId = await _agentClient.ApplyLocaleAsync(vm.Name);
            if (taskId == null)
            {
                await _agentClient.DisconnectProgressHubAsync();
                ShowError("Failed to start locale task");
                return;
            }

            (bool success, string? error) = await completion.Task;
            await _agentClient.DisconnectProgressHubAsync();

            if (success)
                ShowSuccess("Locale applied to " + vm.Name);
            else
                ShowError("Locale application failed: " + (error ?? "unknown error"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply locale to {VmName}", vm.Name);
            ShowError("Failed to apply locale: " + ex.Message);
        }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
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
            (_, _, status) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
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

    [RelayCommand]
    public async Task CloneFromSnapshotAsync(VmSnapshot snapshot)
    {
        string defaultName = (snapshot.VmName + "-" + snapshot.Name).Replace(" ", "-");
        string? newName = RequestVmName != null ? await RequestVmName(defaultName) : defaultName;
        if (string.IsNullOrWhiteSpace(newName))
            return;

        VmInstanceViewModel? vm = Vms.FirstOrDefault(v => v.Name == snapshot.VmName);
        if (vm != null)
        {
            vm.IsBusy = true;
            vm.StatusMessage = "Cloning...";
        }

        try
        {
            await _agentClient.CloneFromSnapshotAsync(snapshot.VmName, snapshot.Id, newName);
            await RefreshAsync();
            ShowSuccess(string.Format(Resources.Status_ClonedFormat, newName, snapshot.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clone from snapshot {SnapshotName} on VM {VmName}",
                snapshot.Name,
                snapshot.VmName
            );
            ShowError(string.Format(Resources.Error_CloneFailedFormat, ex.Message));
        }
        finally
        {
            if (vm != null)
            {
                vm.IsBusy = false;
                vm.StatusMessage = "";
            }
        }
    }

    [RelayCommand]
    public async Task PushSnapshotAsync(VmSnapshot snapshot)
    {
        VmInstanceViewModel? vm = Vms.FirstOrDefault(v => v.Name == snapshot.VmName);
        if (vm != null)
        {
            vm.IsBusy = true;
            vm.StatusMessage = "Pushing snapshot...";
        }

        try
        {
            await _agentClient.PushSnapshotAsync(snapshot.VmName, snapshot.Id);
            ShowSuccess("Push started for " + snapshot.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to push snapshot {SnapshotName} for VM {VmName}",
                snapshot.Name,
                snapshot.VmName
            );
            ShowError("Push failed: " + ex.Message);
        }
        finally
        {
            if (vm != null)
            {
                vm.IsBusy = false;
                vm.StatusMessage = "";
            }
        }
    }

    [RelayCommand]
    public async Task DeleteSnapshotAsync(VmSnapshot snapshot)
    {
        if (ConfirmAction != null)
        {
            bool confirmed = await ConfirmAction(
                string.Format(Resources.Confirm_DeleteSnapshotTitleFormat, snapshot.Name),
                Resources.Confirm_DeleteSnapshotMessage
            );
            if (!confirmed)
                return;
        }

        VmInstanceViewModel? vm = Vms.FirstOrDefault(v => v.Name == snapshot.VmName);
        if (vm != null)
        {
            vm.IsBusy = true;
            vm.StatusMessage = "Deleting snapshot...";
        }

        try
        {
            await _agentClient.DeleteSnapshotAsync(snapshot.VmName, snapshot.Id);
            ShowSuccess(string.Format(Resources.Status_SnapshotDeletedFormat, snapshot.Name));

            VmInstanceViewModel? parentVm = Vms.FirstOrDefault(v => v.Name == snapshot.VmName);
            if (parentVm != null)
                await LoadSnapshotsForVmAsync(parentVm);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete snapshot {SnapshotName} for VM {VmName}",
                snapshot.Name,
                snapshot.VmName
            );
            ShowError(string.Format(Resources.Error_DeleteFailedFormat, ex.Message));
        }
        finally
        {
            if (vm != null)
            {
                vm.IsBusy = false;
                vm.StatusMessage = "";
            }
        }
    }

    private async Task CheckRdpReadinessAsync(VmInstanceViewModel vm)
    {
        try
        {
            bool ready = await _agentClient.IsRdpReadyAsync(vm.Name);
            if (ready)
                return;

            vm.IsBusy = true;
            vm.SetStateOverride("Starting");
            vm.StatusMessage = "Waiting for RDP...";
            await PollRdpReadyAsync(vm, TimeSpan.FromMinutes(5));
            vm.SetStateOverride(null);
        }
        catch { }
        finally
        {
            vm.IsBusy = false;
            vm.StatusMessage = "";
            vm.SetStateOverride(null);
        }
    }

    internal FeedConfiguration? ResolvePushFeed(VmOrigin? origin, AppSettings settings) => null;

    [RelayCommand]
    public void SaveNoteForVm(VmInstanceViewModel vm)
    {
        _ = _agentClient.SaveNotesAsync(vm.Name, vm.Notes ?? "");
    }

    private async Task PollVmStateAsync(
        VmInstanceViewModel vm,
        string targetState,
        TimeSpan timeout
    )
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            List<VmInstance> vms = await _agentClient.GetVmsAsync();
            VmInstance? updated = vms.FirstOrDefault(v => v.Name == vm.Name);
            if (updated?.State == targetState)
                return;
            await Task.Delay(3000);
        }
    }

    private async Task PollRdpReadyAsync(VmInstanceViewModel vm, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                bool ready = await _agentClient.IsRdpReadyAsync(vm.Name);
                if (ready)
                    return;
            }
            catch { }
            await Task.Delay(2000);
        }
    }

    public async Task LoadShadowSessionsAsync(VmInstanceViewModel vm)
    {
        vm.SessionsLoading = true;
        vm.ShadowSessions.Clear();
        try
        {
            RdpShadowSessionsResponse response = await _agentClient.GetShadowSessionsAsync(vm.Name);
            vm.SessionsVmIp = response.VmIp;
            foreach (RdpShadowSession session in response.Sessions)
                vm.ShadowSessions.Add(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sessions for VM {VmName}", vm.Name);
            ShowError("Failed to load sessions: " + ex.Message);
        }
        finally
        {
            vm.SessionsLoading = false;
        }
    }

    public void LaunchShadow(VmInstanceViewModel vm, RdpShadowSession session, bool noConsentPrompt)
    {
        try
        {
            AgentClient.LaunchShadowSession(vm.SessionsVmIp, session.SessionId, noConsentPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch shadow session");
            ShowError("Failed to launch shadow: " + ex.Message);
        }
    }

    private void RebuildGroups(List<VmInstanceViewModel> vms)
    {
        GroupedVms.Clear();
        foreach (IGrouping<string, VmInstanceViewModel> group in vms.GroupBy(v => v.GroupKey))
        {
            string displayName = group.Key switch
            {
                "HyperV" => "Hyper-V",
                "HyperV_External" => "Hyper-V (External)",
                "KVM" => "KVM",
                "KVM_External" => "KVM (External)",
                "Proxmox" => "Proxmox VE",
                "Proxmox_External" => "Proxmox VE (External)",
                _ => group.Key,
            };
            GroupedVms.Add(
                new VmGroup
                {
                    Name = $"{displayName} ({group.Count()})",
                    IsExpanded = group.Key is "HyperV" or "KVM" or "Proxmox",
                    Items = new ObservableCollection<VmInstanceViewModel>(group),
                }
            );
        }
    }

    private async Task LoadAllSnapshotCountsAsync(List<VmInstanceViewModel> vms)
    {
        foreach (
            VmInstanceViewModel vm in vms.Where(v => v.Backend is "HyperV" or "KVM" or "Proxmox")
        )
        {
            try
            {
                List<VmSnapshot> snapshots = await _agentClient.GetSnapshotsAsync(vm.Name);
                vm.SnapshotCount = snapshots.Count;
                vm.SnapshotCountLoaded = true;
            }
            catch { }
        }
    }
}
