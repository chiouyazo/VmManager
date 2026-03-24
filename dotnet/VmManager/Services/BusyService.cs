using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VmManager.Services;

/// <summary>
/// Singleton that tracks whether any long-running operation is in progress.
/// The MainWindow binds its overlay visibility to <see cref="IsBusy"/>.
/// </summary>
public class BusyService : INotifyPropertyChanged
{
    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
                return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
