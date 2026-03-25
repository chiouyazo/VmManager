using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VmManager.Services;
using VmManager.Views.Pages;

namespace VmManager.Views;

/// <summary>
/// Code-behind for the main window. Manages sidebar navigation between pages
/// and shows backend availability in the status bar.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ImagesPage _imagesPage;
    private readonly MyVmsPage _myVmsPage;
    private readonly SnapshotsPage _snapshotsPage;
    private readonly SettingsPage _settingsPage;
    private readonly PreflightService _preflight;
    private readonly SettingsService _settings;

    // Tracks which nav button is currently active
    private Button? _activeNavButton;
    private readonly DispatcherTimer _statusTimer;

    /// <summary>Exposed so the XAML overlay can bind to IsBusy.</summary>
    public BusyService Busy { get; }

    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x10, 0x7C, 0x10));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xC8, 0x10, 0x2E));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(0x99, 0x99, 0x99));

    public MainWindow(
        BusyService busyService,
        PreflightService preflightService,
        SettingsService settingsService,
        ImagesPage imagesPage,
        MyVmsPage myVmsPage,
        SnapshotsPage snapshotsPage,
        SettingsPage settingsPage
    )
    {
        Busy = busyService;
        _preflight = preflightService;
        _settings = settingsService;
        _imagesPage = imagesPage;
        _myVmsPage = myVmsPage;
        _snapshotsPage = snapshotsPage;
        _settingsPage = settingsPage;

        DataContext = this;
        InitializeComponent();

        // Set version from assembly
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
            VersionText.Text = $"VM Manager v{version.Major}.{version.Minor}.{version.Build}";

        // Start on the Images page
        NavigateTo(NavImages, _imagesPage);

        // Auto-refresh current page when window regains focus
        Activated += OnWindowActivated;

        // Check backend status on startup
        _ = RefreshBackendStatusAsync();
        RefreshConnectionStatus();

        // Periodic status bar refresh (every 60 seconds)
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _statusTimer.Tick += async (_, _) =>
        {
            await RefreshBackendStatusAsync();
            RefreshConnectionStatus();
        };
        _statusTimer.Start();
    }

    private async Task RefreshBackendStatusAsync()
    {
        try
        {
            var hyperVOk = await _preflight.IsHyperVAvailableAsync();
            HyperVIndicator.Fill = hyperVOk ? GreenBrush : RedBrush;
            HyperVStatusText.Text = hyperVOk ? "Hyper-V: Verfügbar" : "Hyper-V: Nicht verfügbar";
        }
        catch
        {
            HyperVIndicator.Fill = RedBrush;
            HyperVStatusText.Text = "Hyper-V: Nicht verfügbar";
        }

        try
        {
            var dockerOk = await _preflight.IsDockerAvailableAsync();
            DockerIndicator.Fill = dockerOk ? GreenBrush : GrayBrush;
            DockerStatusText.Text = dockerOk ? "Docker: Verfügbar" : "Docker: Nicht installiert";
        }
        catch
        {
            DockerIndicator.Fill = GrayBrush;
            DockerStatusText.Text = "Docker: Nicht installiert";
        }
    }

    private Page? _currentPage;
    private DateTime _lastRefresh = DateTime.MinValue;

    /// <summary>When set, the Snapshots page will auto-select this VM after navigation.</summary>
    public string? PendingSnapshotVmName { get; set; }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        // Debounce - don't refresh if we just did (< 3 seconds ago)
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 3)
            return;
        _lastRefresh = DateTime.Now;

        // Refresh the current page's data
        if (_currentPage == _myVmsPage)
        {
            var vm = _myVmsPage.DataContext as ViewModels.MyVmsViewModel;
            if (vm is { IsLoading: false, IsBusy: false })
                _ = vm.RefreshAsync();
        }
        else if (_currentPage == _snapshotsPage)
        {
            var vm = _snapshotsPage.DataContext as ViewModels.SnapshotsViewModel;
            if (vm is { IsLoadingVms: false, IsBusy: false })
                _ = vm.LoadVmsAsync();
        }
    }

    private void RefreshConnectionStatus()
    {
        try
        {
            var settings = _settings.Load();
            var hasRegistry = !string.IsNullOrWhiteSpace(settings.RegistryUrl);
            if (hasRegistry)
            {
                ConnectionStatusPanel.Visibility = Visibility.Visible;
                ConnectionIndicator.Fill = GreenBrush;
                ConnectionStatusText.Text = $"Registry: {settings.RegistryUrl}";
            }
            else
            {
                ConnectionStatusPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            ConnectionStatusPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void VersionText_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "https://github.com/chiouyazo/VmManager",
                UseShellExecute = true,
            }
        );
    }

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        var page = btn.Tag?.ToString() switch
        {
            "Images" => (Page)_imagesPage,
            "MyVMs" => _myVmsPage,
            "Snapshots" => _snapshotsPage,
            "Settings" => _settingsPage,
            _ => null,
        };

        if (page is not null)
            NavigateTo(btn, page);
    }

    /// <summary>Navigates to a page by tag name (e.g. "MyVMs", "Images").</summary>
    public void NavigateToPage(string tag)
    {
        var (btn, page) = tag switch
        {
            "Images" => ((Button)NavImages, (Page)_imagesPage),
            "MyVMs" => (NavMyVMs, _myVmsPage),
            "Snapshots" => (NavSnapshots, _snapshotsPage),
            "Settings" => (NavSettings, _settingsPage),
            _ => (null!, null!),
        };

        if (page is not null)
            NavigateTo(btn, page);
    }

    private async void NavigateTo(Button navButton, Page page)
    {
        if (_activeNavButton is not null)
            _activeNavButton.Style = (Style)FindResource("NavButtonStyle");

        navButton.Style = (Style)FindResource("NavButtonActiveStyle");
        _activeNavButton = navButton;
        _currentPage = page;

        ContentFrame.Navigate(page);

        // Auto-select VM on snapshots page if requested
        if (page == _snapshotsPage && PendingSnapshotVmName != null)
        {
            var vmName = PendingSnapshotVmName;
            PendingSnapshotVmName = null;
            var vm = _snapshotsPage.DataContext as ViewModels.SnapshotsViewModel;
            if (vm != null)
                await vm.SelectVmByNameAsync(vmName);
        }
    }
}
