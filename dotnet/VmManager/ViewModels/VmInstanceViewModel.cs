using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VmManager.ViewModels;

/// <summary>
/// ViewModel wrapper around <see cref="VmInstance"/> that adds observable UI state
/// (snapshots, rename, etc.) while keeping the Contracts model a pure POCO.
/// </summary>
public partial class VmInstanceViewModel : ObservableObject
{
    public VmInstance Data { get; }

    public VmInstanceViewModel(VmInstance data)
    {
        Data = data;
    }

    // Pass-through properties from Data
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

    public void UpdateData(VmInstance newData)
    {
        bool stateChanged = Data.State != newData.State;
        Data.State = newData.State;
        Data.MemoryAssigned = newData.MemoryAssigned;
        Data.Uptime = newData.Uptime;
        Data.IsManaged = newData.IsManaged;
        Data.Origin = newData.Origin;
        Data.Notes = newData.Notes;
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

    public ObservableCollection<VmSnapshot> Snapshots { get; } =
        new ObservableCollection<VmSnapshot>();
}
