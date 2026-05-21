using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VmManager.Contracts.Models;
using VmManager.Services;

namespace VmManager.ViewModels;

public partial class VmInstanceViewModel : ObservableObject
{
    public VmInstance Data { get; }
    private readonly PermissionService? _permissionService;

    public VmInstanceViewModel(VmInstance data, PermissionService? permissionService = null)
    {
        Data = data;
        _permissionService = permissionService;
    }

    public string Name => Data.Name;

    private string? _stateOverride;
    public string State => _stateOverride ?? Data.State;
    public bool IsRunning => State == "Running";
    public bool IsOff => State == "Off";

    public void SetStateOverride(string? state)
    {
        _stateOverride = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsOff));
    }

    public long MemoryAssigned => Data.MemoryAssigned;
    public TimeSpan Uptime => Data.Uptime;
    public string Backend => Data.Backend;
    public bool IsManaged => Data.IsManaged;
    public VmOrigin? Origin => Data.Origin;
    public string GroupKey => Data.GroupKey;
    public string MemoryDisplay => Data.MemoryDisplay;
    public string? OriginDisplay => Data.OriginDisplay;

    public string Owner => Data.Owner;
    public List<string> SharedWith => Data.SharedWith;

    public bool IsOwnedByCurrentUser =>
        _permissionService != null
        && string.Equals(
            Data.Owner,
            _permissionService.Username,
            StringComparison.OrdinalIgnoreCase
        );

    public bool IsSharedWithCurrentUser =>
        _permissionService != null
        && !IsOwnedByCurrentUser
        && Data.SharedWith.Any(u =>
            string.Equals(u, _permissionService.Username, StringComparison.OrdinalIgnoreCase)
        );

    public bool CanStart => Data.EffectivePermissions.Contains(Permission.VmStart);
    public bool CanStop => Data.EffectivePermissions.Contains(Permission.VmStop);
    public bool CanDelete =>
        Data.EffectivePermissions.Contains(Permission.VmDelete) && IsOwnedByCurrentUser;
    public bool CanRename =>
        Data.EffectivePermissions.Contains(Permission.VmRename) && IsOwnedByCurrentUser;
    public bool CanReset => Data.EffectivePermissions.Contains(Permission.VmReset);
    public bool CanConnect => Data.EffectivePermissions.Contains(Permission.RdpConnect);
    public bool CanCreateSnapshot => Data.EffectivePermissions.Contains(Permission.SnapshotCreate);
    public bool CanShare =>
        IsOwnedByCurrentUser || (_permissionService != null && _permissionService.IsAdmin);
    public bool CanShadow => _permissionService != null && _permissionService.IsAdmin;

    [ObservableProperty]
    private bool _sessionsLoading;

    [ObservableProperty]
    private string _sessionsVmIp = "";

    public ObservableCollection<RdpShadowSession> ShadowSessions { get; } = [];

    public void UpdateData(VmInstance newData)
    {
        bool stateChanged = Data.State != newData.State;
        Data.State = newData.State;
        Data.MemoryAssigned = newData.MemoryAssigned;
        Data.Uptime = newData.Uptime;
        Data.IsManaged = newData.IsManaged;
        Data.Origin = newData.Origin;
        Data.Notes = newData.Notes;
        Data.Owner = newData.Owner;
        Data.SharedWith = newData.SharedWith;
        Data.EffectivePermissions = newData.EffectivePermissions;
        if (stateChanged)
        {
            if (_stateOverride == null)
            {
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsOff));
            }
            OnPropertyChanged(nameof(MemoryDisplay));
        }
        OnPropertyChanged(nameof(Uptime));
        OnPropertyChanged(nameof(Owner));
        OnPropertyChanged(nameof(SharedWith));
        OnPropertyChanged(nameof(IsOwnedByCurrentUser));
        OnPropertyChanged(nameof(IsSharedWithCurrentUser));
    }

    public string Notes
    {
        get => Data.Notes;
        set
        {
            if (Data.Notes != value)
            {
                Data.Notes = value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _pendingRename = "";

    [ObservableProperty]
    private string _newSnapshotName = "";

    [ObservableProperty]
    private int _snapshotCount;

    [ObservableProperty]
    private bool _snapshotCountLoaded;

    [ObservableProperty]
    private bool _snapshotsLoaded;

    [ObservableProperty]
    private bool _snapshotsExpanded;

    [ObservableProperty]
    private bool _isExpanded;

    public bool ShowSnapshots => IsExpanded && SnapshotsLoaded;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSnapshots));
    }

    partial void OnSnapshotsLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSnapshots));
    }

    public ObservableCollection<VmSnapshot> Snapshots { get; } = [];
}
