using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VmManager.ViewModels;

/// <summary>
/// ViewModel for the setup wizard dialog. Manages the three-step wizard flow
/// and collects initial configuration (VM defaults, locale, feeds).
/// </summary>
public partial class SetupWizardViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(StepIndicator))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    private int _currentStep;

    [ObservableProperty]
    private ObservableCollection<FeedEntryViewModel> _feeds = [];

    [ObservableProperty]
    private string _localVmPath = @"C:\VMs";

    [ObservableProperty]
    private int _memoryMb = 4096;

    [ObservableProperty]
    private int _cpuCount = Math.Min(4, Environment.ProcessorCount);

    [ObservableProperty]
    private int _maxCpuCount = Environment.ProcessorCount;

    [ObservableProperty]
    private bool _applyLocale;

    [ObservableProperty]
    private string? _selectedLocale = SettingsViewModel.LocaleMap.Keys.FirstOrDefault();

    [ObservableProperty]
    private string? _selectedKeyboard = SettingsViewModel.KeyboardMap.Keys.FirstOrDefault();

    [ObservableProperty]
    private string? _selectedTimezone = SettingsViewModel
        .TimezoneMap.FirstOrDefault(kv => kv.Value == TimeZoneInfo.Local.Id)
        .Key;

    [ObservableProperty]
    private string _username = "Administrator";

    [ObservableProperty]
    private string _password = "Admin123!";

    public bool IsFirstStep => CurrentStep == 0;
    public bool IsLastStep => CurrentStep == 2;
    public string StepIndicator => string.Format(Resources.Wizard_StepFormat, CurrentStep + 1);
    public string NextButtonText =>
        CurrentStep == 2 ? Resources.Dialog_Finish : Resources.Dialog_Next;

    // Reuse maps from SettingsViewModel
    public IReadOnlyList<string> LocaleNames { get; } = SettingsViewModel.LocaleMap.Keys.ToList();
    public IReadOnlyList<string> KeyboardNames { get; } =
        SettingsViewModel.KeyboardMap.Keys.ToList();
    public IReadOnlyList<string> TimezoneNames { get; } =
        SettingsViewModel.TimezoneMap.Keys.ToList();

    /// <summary>The result settings. Built when the user clicks Finish.</summary>
    public AppSettings? Result { get; private set; }

    /// <summary>Raised when the wizard should close with a positive result.</summary>
    public event Action? RequestClose;

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep < 2)
        {
            CurrentStep++;
            return;
        }

        // Finish -> build the result
        string localeId =
            SelectedLocale is string loc
            && SettingsViewModel.LocaleMap.TryGetValue(loc, out string? lid)
                ? lid
                : "";
        string keyboardId =
            SelectedKeyboard is string kb
            && SettingsViewModel.KeyboardMap.TryGetValue(kb, out string? kid)
                ? kid
                : "";
        string timezoneId =
            SelectedTimezone is string tz
            && SettingsViewModel.TimezoneMap.TryGetValue(tz, out string? tzid)
                ? tzid
                : "";

        Result = new AppSettings
        {
            LocalVmPath = LocalVmPath.Trim(),
            DefaultMemoryMb = MemoryMb,
            DefaultCpuCount = CpuCount,
            ApplyLocaleOnCreate = ApplyLocale,
            DefaultLocale = localeId,
            DefaultKeyboardLayout = keyboardId,
            DefaultTimezone = timezoneId,
            DefaultVmUsername = Username.Trim(),
            DefaultVmPassword = Password,
            HasCompletedSetup = true,
        };

        foreach (FeedEntryViewModel feed in Feeds)
            Result.Feeds.Add(feed.ToConfiguration());

        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void AddFeed()
    {
        Feeds.Add(new FeedEntryViewModel { Name = Resources.Settings_NewFeedName });
    }

    [RelayCommand]
    private void RemoveFeed(FeedEntryViewModel feed)
    {
        Feeds.Remove(feed);
    }
}
