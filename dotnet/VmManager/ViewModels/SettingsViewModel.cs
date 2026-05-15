using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VmManager.Services;

namespace VmManager.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AgentSettingsService _agentSettings;
    private readonly ILogger<SettingsViewModel> _logger;

    private AgentClient _agentClient => App.AgentClient!;

    public static readonly Dictionary<string, string> LocaleMap = new Dictionary<string, string>()
    {
        { "English (United States)", "en-US" },
        { "German (Germany)", "de-DE" },
        { "French (France)", "fr-FR" },
        { "Spanish (Spain)", "es-ES" },
        { "Italian (Italy)", "it-IT" },
        { "Dutch (Netherlands)", "nl-NL" },
        { "Portuguese (Brazil)", "pt-BR" },
        { "Polish (Poland)", "pl-PL" },
        { "Czech (Czech Republic)", "cs-CZ" },
        { "Swedish (Sweden)", "sv-SE" },
        { "Russian (Russia)", "ru-RU" },
        { "Turkish (Turkey)", "tr-TR" },
        { "Japanese (Japan)", "ja-JP" },
    };

    public static readonly Dictionary<string, string> KeyboardMap = new Dictionary<string, string>()
    {
        { "US (QWERTY)", "00000409" },
        { "German (QWERTZ)", "00000407" },
        { "UK", "00000809" },
        { "French (AZERTY)", "0000040C" },
        { "Spanish", "0000040A" },
        { "Italian", "00000410" },
        { "Dutch", "00000413" },
        { "Swiss German", "00000807" },
        { "Polish", "00000415" },
        { "Czech", "00000405" },
        { "Swedish", "0000041D" },
        { "Russian", "00000419" },
        { "Turkish", "0000041F" },
    };

    public static readonly Dictionary<string, string> TimezoneMap = TimeZoneInfo
        .GetSystemTimeZones()
        .GroupBy(tz => tz.DisplayName)
        .ToDictionary(g => g.Key, g => g.First().Id);

    public IReadOnlyList<string> LocaleNames { get; } = LocaleMap.Keys.ToList();
    public IReadOnlyList<string> KeyboardNames { get; } = KeyboardMap.Keys.ToList();
    public IReadOnlyList<string> TimezoneNames { get; } = TimezoneMap.Keys.ToList();

    [ObservableProperty]
    private ObservableCollection<FeedEntryViewModel> _feeds = [];

    [ObservableProperty]
    private string _localVmPath = "";

    [ObservableProperty]
    private int _memoryMb = 4096;

    [ObservableProperty]
    private int _cpuCount = 4;

    [ObservableProperty]
    private int _maxCpuCount = Environment.ProcessorCount;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _applyLocale;

    [ObservableProperty]
    private string? _selectedLocale;

    [ObservableProperty]
    private string? _selectedKeyboard;

    [ObservableProperty]
    private string? _selectedTimezone;

    [ObservableProperty]
    private bool _renameComputerToVmName = true;

    [ObservableProperty]
    private string _postCreationScript = "";

    [ObservableProperty]
    private string _postStartupScript = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _showStatus;

    public bool IsLocalAgent => App.AgentConnection?.IsLocal ?? true;

    public bool IsConnected => App.AgentClient != null;

    public Func<string?, Task<string?>>? RequestBrowseFolder { get; set; }
    public Action? OnAgentsSaved { get; set; }
    public Action? OnSettingsSaved { get; set; }

    [ObservableProperty]
    private ObservableCollection<AgentConfiguration> _agents = [];

    public SettingsViewModel(AgentSettingsService agentSettings, ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(agentSettings);
        ArgumentNullException.ThrowIfNull(logger);
        _agentSettings = agentSettings;
        _logger = logger;
    }

    [RelayCommand]
    private void AddAgent()
    {
        AgentConfiguration newAgent = new AgentConfiguration
        {
            Name = "New Agent",
            Url = "http://",
        };
        Agents.Add(newAgent);
    }

    [RelayCommand]
    private void RemoveAgent(AgentConfiguration agent)
    {
        if (agent.IsLocal)
            return;
        Agents.Remove(agent);
    }

    [RelayCommand]
    private async Task TestAgentAsync(AgentConfiguration agent)
    {
        try
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
            using HttpClient http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            if (!string.IsNullOrEmpty(agent.Username) && !string.IsNullOrEmpty(agent.Password))
            {
                string credentials = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes(agent.Username + ":" + agent.Password)
                );
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            HttpResponseMessage response = await http.GetAsync(agent.Url.TrimEnd('/') + "/health");
            if (response.IsSuccessStatusCode)
                ShowStatusMessage(false, agent.Name + ": connected");
            else
                ShowStatusMessage(true, agent.Name + ": returned " + (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent connection test failed for {AgentName}", agent.Name);
            ShowStatusMessage(true, agent.Name + ": " + ex.Message);
        }
    }

    public async Task LoadSettingsAsync()
    {
        List<AgentConfiguration> agents = _agentSettings.Load();
        Agents = new ObservableCollection<AgentConfiguration>(agents);

        if (App.AgentClient == null)
            return;

        AppSettings settings = await _agentClient.GetSettingsAsync();
        Feeds.Clear();
        foreach (FeedConfiguration feed in settings.Feeds)
            Feeds.Add(FeedEntryViewModel.FromConfiguration(feed));

        LocalVmPath = settings.LocalVmPath;
        MemoryMb = settings.DefaultMemoryMb;
        MaxCpuCount = Environment.ProcessorCount;
        CpuCount = Math.Min(settings.DefaultCpuCount, Environment.ProcessorCount);
        Username = settings.DefaultVmUsername;
        Password = settings.DefaultVmPassword;
        ApplyLocale = settings.ApplyLocaleOnCreate;
        SelectedLocale = LocaleMap.FirstOrDefault(kv => kv.Value == settings.DefaultLocale).Key;
        SelectedKeyboard = KeyboardMap
            .FirstOrDefault(kv => kv.Value == settings.DefaultKeyboardLayout)
            .Key;
        SelectedTimezone = TimezoneMap
            .FirstOrDefault(kv => kv.Value == settings.DefaultTimezone)
            .Key;
        RenameComputerToVmName = settings.RenameComputerToVmName;
        PostCreationScript = settings.PostCreationScript;
        PostStartupScript = settings.PostStartupScript;
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

    [RelayCommand]
    private async Task TestConnectionAsync(FeedEntryViewModel feed)
    {
        feed.IsTesting = true;
        feed.ConnectionStatus = Resources.Settings_Testing;

        try
        {
            FeedConfiguration config = feed.ToConfiguration();
            config.Url = NormalizeUrl(config.Url);
            bool reachable = await _agentClient.TestFeedAsync(config);

            feed.ConnectionStatus = reachable
                ? Resources.Settings_Connected
                : Resources.Settings_Unreachable;
            feed.IsConnected = reachable;
        }
        catch (Exception ex)
        {
            feed.ConnectionStatus = string.Format(
                Resources.Settings_ConnectionFailedFormat,
                ex.Message
            );
            feed.IsConnected = false;
            _logger.LogWarning(ex, "Test connection failed for feed {FeedName}", feed.Name);
        }
        finally
        {
            feed.IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task DiscoverReposAsync(FeedEntryViewModel feed)
    {
        feed.IsTesting = true;
        feed.ConnectionStatus = Resources.Settings_Discovering;
        feed.DiscoveredRepos.Clear();

        try
        {
            FeedConfiguration config = feed.ToConfiguration();
            config.Url = NormalizeUrl(config.Url);
            List<string> repos = await _agentClient.DiscoverRepositoriesAsync(config);

            foreach (string repo in repos)
                feed.DiscoveredRepos.Add(repo);

            feed.ConnectionStatus = string.Format(
                Resources.Settings_ReposFoundFormat,
                repos.Count,
                repos.Count == 1 ? "y" : "ies"
            );
            feed.IsConnected = true;
        }
        catch (Exception ex)
        {
            feed.ConnectionStatus = string.Format(
                Resources.Settings_ConnectionFailedFormat,
                ex.Message
            );
            feed.IsConnected = false;
            _logger.LogWarning(ex, "Discover repos failed for feed {FeedName}", feed.Name);
        }
        finally
        {
            feed.IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _agentSettings.Save(Agents.ToList());
            OnAgentsSaved?.Invoke();

            string? currentSelected = _agentSettings.LoadSelectedAgentId();
            if (currentSelected != null && !Agents.Any(a => a.Id == currentSelected))
            {
                if (Agents.Count > 0)
                    _agentSettings.SaveSelectedAgentId(Agents[0].Id);
                else
                    _agentSettings.SaveSelectedAgentId("");
            }

            if (App.AgentClient != null)
            {
                List<FeedConfiguration> feedConfigs = [];
                foreach (FeedEntryViewModel feed in Feeds)
                {
                    FeedConfiguration config = feed.ToConfiguration();
                    config.Url = NormalizeUrl(config.Url);
                    feedConfigs.Add(config);
                }

                string localeId =
                    SelectedLocale is string localeName
                    && LocaleMap.TryGetValue(localeName, out string? lid)
                        ? lid
                        : "";
                string keyboardId =
                    SelectedKeyboard is string kbName
                    && KeyboardMap.TryGetValue(kbName, out string? kid)
                        ? kid
                        : "";
                string timezoneId =
                    SelectedTimezone is string tzName
                    && TimezoneMap.TryGetValue(tzName, out string? tzid)
                        ? tzid
                        : "";

                AppSettings existing = await _agentClient.GetSettingsAsync();
                existing.Feeds = feedConfigs;
                existing.HasCompletedSetup = true;
                existing.LocalVmPath = LocalVmPath.Trim();
                existing.DefaultMemoryMb = MemoryMb;
                existing.DefaultCpuCount = CpuCount;
                existing.DefaultVmUsername = Username.Trim();
                existing.DefaultVmPassword = Password;
                existing.ApplyLocaleOnCreate = ApplyLocale;
                existing.DefaultLocale = localeId;
                existing.DefaultKeyboardLayout = keyboardId;
                existing.DefaultTimezone = timezoneId;
                existing.RenameComputerToVmName = RenameComputerToVmName;
                existing.PostCreationScript = PostCreationScript;
                existing.PostStartupScript = PostStartupScript;

                await _agentClient.SaveSettingsAsync(existing);
                OnSettingsSaved?.Invoke();
            }

            ShowStatusMessage(false, Resources.Settings_Saved);
        }
        catch (Exception ex)
        {
            ShowStatusMessage(true, string.Format(Resources.Settings_SaveFailedFormat, ex.Message));
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    [RelayCommand]
    private async Task BrowseLocalVmPathAsync()
    {
        if (RequestBrowseFolder == null)
            return;
        string? result = await RequestBrowseFolder.Invoke(LocalVmPath);
        if (result != null)
            LocalVmPath = result;
    }

    private void ShowStatusMessage(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = true;
    }

    internal static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;
        if (
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("\\\\")
            && !Path.IsPathRooted(url)
        )
            url = "http://" + url;
        return url.TrimEnd('/');
    }
}
