using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VmManager.ViewModels;

/// <summary>
/// Observable wrapper implementing <see cref="IBackgroundTask"/>.
/// All property setters dispatch to the UI thread.
/// </summary>
public partial class BackgroundTaskViewModel : ObservableObject, IBackgroundTask
{
    private readonly CancellationTokenSource _cts;

    public string Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private double _progress = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool _isComplete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool _isFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool _isCancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    private bool _isCancellable;

    [ObservableProperty]
    private string? _errorMessage;

    public bool ShowCancelButton => IsCancellable && !IsComplete && !IsFailed && !IsCancelled;

    public bool ShowProgressBar => !IsComplete && !IsFailed && !IsCancelled;

    public bool ShowError => IsFailed;

    public bool IsRunning => !IsComplete && !IsFailed && !IsCancelled;

    public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();

    public IReadOnlyList<string> LogLines => LogEntries;

    public BackgroundTaskViewModel(string title, CancellationTokenSource cts, bool isCancellable)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(cts);
        Id = Guid.NewGuid().ToString("N");
        _title = title;
        _cts = cts;
        _isCancellable = isCancellable;
    }

    [RelayCommand]
    public void Cancel()
    {
        if (IsCancellable && !IsComplete && !IsFailed && !IsCancelled)
        {
            _cts.Cancel();
        }
    }

    internal void SetProgress(double percent, string status)
    {
        Progress = percent;
        Status = status;
    }

    internal void AddLog(string message)
    {
        LogEntries.Add(message);
    }

    internal void SetComplete()
    {
        IsComplete = true;
        Progress = 1.0;
        Status = "Complete";
    }

    internal void SetFailed(string error)
    {
        IsFailed = true;
        ErrorMessage = error;
        Status = "Failed";
    }

    internal void SetCancelled()
    {
        IsCancelled = true;
        Status = "Cancelled";
    }
}
