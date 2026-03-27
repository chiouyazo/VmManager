using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VmManager.Services;

namespace VmManager.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogger<MainWindowViewModel> _logger;

    private AgentClient _agentClient => App.AgentClient!;

    [ObservableProperty]
    private string _backendStatusText = Resources.Status_BackendChecking;

    [ObservableProperty]
    private bool _backendAvailable;

    [ObservableProperty]
    private string _feedStatusText = Resources.Status_FeedsChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedIndicatorActive))]
    private int _feedCount;

    [ObservableProperty]
    private string _versionText = Resources.AppTitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaskStatusDisplayText))]
    private int _activeTaskCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaskStatusDisplayText))]
    private bool _hasActiveTasks;

    [ObservableProperty]
    private bool _hasTasks;

    public string TaskStatusDisplayText =>
        HasActiveTasks ? string.Format("{0} running", ActiveTaskCount) : "Tasks";

    public bool FeedIndicatorActive => FeedCount > 0;

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
            VersionText = string.Format(
                Resources.Status_VersionFormat,
                version.Major,
                version.Minor,
                version.Build
            );
    }

    public async Task RefreshBackendStatusAsync()
    {
        if (App.AgentClient == null)
            return;
        try
        {
            bool healthy = await _agentClient.IsHealthyAsync();
            string backendType = healthy ? await _agentClient.GetBackendTypeAsync() : "Agent";
            string displayName = FormatBackendName(backendType);
            BackendAvailable = healthy;
            BackendStatusText = healthy
                ? $"{displayName}: Available"
                : $"{displayName}: Unavailable";
        }
        catch (Exception ex)
        {
            BackendAvailable = false;
            BackendStatusText = "Agent: Unavailable";
            _logger.LogWarning(ex, "Failed to check agent status");
        }
    }

    private static string FormatBackendName(string backendType)
    {
        return backendType switch
        {
            "HyperV" => "Hyper-V",
            "KVM" => "KVM",
            _ => backendType,
        };
    }

    public async Task RefreshConnectionStatusAsync()
    {
        if (App.AgentClient == null)
            return;
        try
        {
            AppSettings settings = await _agentClient.GetSettingsAsync();
            int totalFeeds = settings.Feeds.Count;
            FeedCount = totalFeeds;

            if (totalFeeds == 0)
            {
                FeedStatusText = Resources.Status_NoFeedsConfigured;
                return;
            }

            FeedStatusText = string.Format(Resources.Status_FeedsConfiguredFormat, totalFeeds);
        }
        catch (Exception ex)
        {
            FeedStatusText = "";
            _logger.LogWarning(ex, "Failed to refresh connection status");
        }
    }
}
