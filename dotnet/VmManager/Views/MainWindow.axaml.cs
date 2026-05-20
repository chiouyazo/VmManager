using System.Diagnostics;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;
using VmManager.Services;
using VmManager.ViewModels;
using VmManager.Views.Controls;
using VmManager.Views.Dialogs;
using VmManager.Views.Pages;

namespace VmManager.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AgentConnection _agentConnection;
    private readonly AgentSettingsService _agentSettings;
    private readonly TrayIconService _trayIconService;
    private readonly PermissionService _permissionService;
    private readonly ILogger<MainWindow> _logger;
    private readonly ImagesPage _imagesPage;
    private readonly MyVmsPage _myVmsPage;
    private readonly SettingsPage _settingsPage;
    private readonly UsersPage _usersPage;
    private readonly TaskPanel _taskPanel;

    private Button? _activeNavButton;
    private readonly DispatcherTimer _statusTimer;

    public BusyService Busy { get; }

    public MainWindow(
        BusyService busyService,
        AgentConnection agentConnection,
        AgentSettingsService agentSettings,
        TrayIconService trayIconService,
        PermissionService permissionService,
        NotificationService notificationService,
        MainWindowViewModel viewModel,
        ImagesPage imagesPage,
        MyVmsPage myVmsPage,
        SettingsPage settingsPage,
        UsersPage usersPage,
        TaskPanel taskPanel,
        ILogger<MainWindow> logger
    )
    {
        ArgumentNullException.ThrowIfNull(busyService);
        ArgumentNullException.ThrowIfNull(agentConnection);
        ArgumentNullException.ThrowIfNull(agentSettings);
        ArgumentNullException.ThrowIfNull(trayIconService);
        ArgumentNullException.ThrowIfNull(permissionService);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(imagesPage);
        ArgumentNullException.ThrowIfNull(myVmsPage);
        ArgumentNullException.ThrowIfNull(settingsPage);
        ArgumentNullException.ThrowIfNull(usersPage);
        ArgumentNullException.ThrowIfNull(taskPanel);
        ArgumentNullException.ThrowIfNull(logger);

        Busy = busyService;
        _agentConnection = agentConnection;
        _agentSettings = agentSettings;
        _trayIconService = trayIconService;
        _permissionService = permissionService;
        _logger = logger;
        _viewModel = viewModel;
        _imagesPage = imagesPage;
        _myVmsPage = myVmsPage;
        _settingsPage = settingsPage;
        _usersPage = usersPage;
        _taskPanel = taskPanel;

        DataContext = this;
        InitializeComponent();

        notificationService.SetManager(NotificationManager);

        Closing += OnClosing;

        TaskPanelHost.Content = _taskPanel;
        StatusBarArea.DataContext = _viewModel;

        PopulateEnvironmentSelector();

        NavigateTo(NavImages, _imagesPage);
        UpdateNavVisibility();

        Activated += OnWindowActivated;

        _ = _viewModel.RefreshBackendStatusAsync();
        _ = _viewModel.RefreshConnectionStatusAsync();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _statusTimer.Tick += async (_, _) =>
        {
            await _viewModel.RefreshBackendStatusAsync();
            _ = _viewModel.RefreshConnectionStatusAsync();
        };
        _statusTimer.Start();
    }

    public void PopulateEnvironmentSelector()
    {
        List<AgentConfiguration> agents = _agentSettings.Load();
        string? selectedId = _agentSettings.LoadSelectedAgentId() ?? "local";

        EnvironmentSelector.SelectionChanged -= EnvironmentSelector_Changed;
        EnvironmentSelector.Items.Clear();

        int selectedIndex = 0;
        int displayIndex = 0;
        bool found = false;
        for (int i = 0; i < agents.Count; i++)
        {
            AgentConfiguration agent = agents[i];

#if CLIENT_ONLY
            if (agent.IsLocal)
                continue;
#elif !WINDOWS
            if (agent.IsLocal)
                continue;
#endif

            ComboBoxItem item = new ComboBoxItem { Content = agent.Name, Tag = agent.Id };
            EnvironmentSelector.Items.Add(item);
            if (agent.Id == selectedId)
            {
                selectedIndex = displayIndex;
                found = true;
            }
            displayIndex++;
        }

        if (!found && EnvironmentSelector.Items.Count > 0)
        {
            selectedIndex = 0;
            if (EnvironmentSelector.Items[0] is ComboBoxItem firstItem)
                _agentSettings.SaveSelectedAgentId(firstItem.Tag?.ToString() ?? "");
        }

        EnvironmentSelector.SelectedIndex = selectedIndex;
        EnvironmentSelector.SelectionChanged += EnvironmentSelector_Changed;
    }

    public void ReconnectCurrentAgent()
    {
        EnvironmentSelector_Changed(EnvironmentSelector, null!);
    }

    private async void EnvironmentSelector_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentSelector.SelectedItem is not ComboBoxItem selected)
            return;

        string agentId = selected.Tag?.ToString() ?? "";
        List<AgentConfiguration> agents = _agentSettings.Load();
        AgentConfiguration? agent = agents.FirstOrDefault(a => a.Id == agentId);
        if (agent == null)
            return;

        _agentSettings.SaveSelectedAgentId(agentId);

        IsEnabled = false;
        Title = "Connecting to " + agent.Name + "...";

        if (_imagesPage.DataContext is ImagesViewModel ivmReset)
            ivmReset.ResetTransientState();

        try
        {
            if (agent.IsLocal)
            {
                _agentConnection.SwitchToLocal();
            }
            else
            {
                await Task.Run(async () =>
                    await _agentConnection.ConnectRemoteAsync(
                        agent.Url,
                        agent.Username,
                        agent.Password,
                        agent.RdpProxyHost
                    )
                );
            }

            App.AgentClient = _agentConnection.Client;
            Title = Properties.Resources.AppTitle;

            AuthenticatedUser currentUser = await App.AgentClient.GetCurrentUserAsync();
            if (currentUser.MustChangePassword)
            {
                ChangePasswordDialog pwDialog = new ChangePasswordDialog();
                bool? changed = await pwDialog.ShowDialog<bool?>(this);
                if (changed != true)
                {
                    EnvironmentSelector.SelectedIndex = 0;
                    return;
                }
            }

            await _permissionService.RefreshAsync();
            UpdateNavVisibility();

            await RefreshAllPagesAsync();
            _ = _viewModel.RefreshBackendStatusAsync();
            _ = _viewModel.RefreshConnectionStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to agent {AgentName}", agent.Name);
            Title = Properties.Resources.AppTitle;

            string errorMessage = ex switch
            {
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } =>
                    $"Invalid credentials for {agent.Name}.",
                HttpRequestException httpEx =>
                    $"Could not reach {agent.Name} at {agent.Url}:\n{httpEx.Message}",
                TimeoutException => $"Connection to {agent.Name} timed out.",
                InvalidOperationException when ex.Message.Contains("not reachable") =>
                    $"{agent.Name} at {agent.Url} is not reachable.\nCheck the URL and ensure the agent is running.",
                _ => $"Failed to connect to {agent.Name}:\n{ex.Message}",
            };

            var dlg = new ConfirmDialog(
                "Connection Failed",
                errorMessage,
                isDangerous: false,
                confirmText: "OK",
                cancelText: null
            );
            await dlg.ShowDialog<bool?>(this);
            EnvironmentSelector.SelectedIndex = 0;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void UpdateNavVisibility()
    {
        NavImages.IsVisible = _permissionService.CanSeeMarketplace;
        NavUsers.IsVisible = _permissionService.IsAdmin;

        if (_currentPage == _imagesPage && !_permissionService.CanSeeMarketplace)
            NavigateTo(NavMyVMs, _myVmsPage);
        if (_currentPage == _usersPage && !_permissionService.IsAdmin)
            NavigateTo(NavMyVMs, _myVmsPage);
    }

    private UserControl? _currentPage;
    private DateTime _lastRefresh = DateTime.MinValue;

    private async Task RefreshAllPagesAsync()
    {
        if (_imagesPage.DataContext is ImagesViewModel ivm)
            await ivm.LoadCatalogAsync();
        if (_myVmsPage.DataContext is MyVmsViewModel mvm)
            await mvm.RefreshAsync();
        if (_settingsPage.DataContext is SettingsViewModel svm)
            await svm.LoadSettingsAsync();
    }

    public string? PendingMarketplaceImageId { get; set; }
    public string? PendingMyVmsMessage { get; set; }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (App.IsShuttingDown)
            return;

        e.Cancel = true;
        Hide();
        _trayIconService.Show();
        _logger.LogInformation("Main window hidden to tray");
    }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 3)
            return;
        _lastRefresh = DateTime.Now;

        if (_currentPage == _myVmsPage)
        {
            MyVmsViewModel? vm = _myVmsPage.DataContext as MyVmsViewModel;
            if (vm is { IsLoading: false, IsBusy: false })
                _ = vm.RefreshAsync();
        }
    }

    private void TaskIndicator_Click(object? sender, RoutedEventArgs e)
    {
        TaskPanelPopup.IsOpen = !TaskPanelPopup.IsOpen;
    }

    private void FeedStatus_Click(object? sender, PointerPressedEventArgs e)
    {
        NavigateToPage("Settings");
    }

    private void VersionText_Click(object? sender, PointerPressedEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "https://github.com/chiouyazo/VmManager",
                UseShellExecute = true,
            }
        );
    }

    private void NavItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        UserControl? page = btn.Tag?.ToString() switch
        {
            "Images" => _imagesPage,
            "MyVMs" => _myVmsPage,
            "Settings" => _settingsPage,
            "Users" => _usersPage,
            _ => null,
        };

        if (page is not null)
            NavigateTo(btn, page);
    }

    public void NavigateToPage(string tag)
    {
        (Button? btn, UserControl? page) = tag switch
        {
            "Images" => ((Button)NavImages, (UserControl)_imagesPage),
            "MyVMs" => (NavMyVMs, _myVmsPage),
            "Settings" => (NavSettings, _settingsPage),
            "Users" => (NavUsers, _usersPage),
            _ => (null!, null!),
        };

        if (page is not null)
            NavigateTo(btn, page);
    }

    private async void NavigateTo(Button navButton, UserControl page)
    {
        if (_activeNavButton is not null)
        {
            _activeNavButton.Theme = (ControlTheme?)this.FindResource("NavButtonStyle");
            _activeNavButton.Background = Avalonia.Media.Brushes.Transparent;
        }

        navButton.Theme = (ControlTheme?)this.FindResource("NavButtonActiveStyle");
        _activeNavButton = navButton;
        _currentPage = page;

        ContentArea.Content = page;

        if (
            page == _imagesPage
            && App.AgentClient != null
            && _imagesPage.DataContext is ImagesViewModel ivm2
        )
            _ = ivm2.LoadCatalogAsync();
        if (
            page == _myVmsPage
            && App.AgentClient != null
            && _myVmsPage.DataContext is MyVmsViewModel mvm2
        )
            _ = mvm2.RefreshAsync();
        if (page == _settingsPage && _settingsPage.DataContext is SettingsViewModel svm2)
            _ = svm2.LoadSettingsAsync();
        if (page == _usersPage && _usersPage.DataContext is UsersViewModel uvm2)
            _ = uvm2.LoadUsersAsync();

        if (page == _imagesPage && PendingMarketplaceImageId != null)
        {
            string imageId = PendingMarketplaceImageId;
            PendingMarketplaceImageId = null;
            if (_imagesPage.DataContext is ImagesViewModel ivm)
                ivm.HighlightImageId = imageId;
        }

        if (page == _myVmsPage && PendingMyVmsMessage != null)
        {
            string message = PendingMyVmsMessage;
            PendingMyVmsMessage = null;
            if (_myVmsPage.DataContext is MyVmsViewModel mvm)
            {
                mvm.IsError = false;
                mvm.StatusMessage = message;
                mvm.ShowStatus = true;
            }
        }
    }
}
