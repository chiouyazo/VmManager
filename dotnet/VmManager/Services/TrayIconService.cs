using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace VmManager.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService> _logger;
    private Window? _mainWindow;
    private DispatcherTimer? _statusTimer;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusMenuItem;
    private NativeMenuItem? _sessionsMenuItem;

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void Initialize(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        _mainWindow = mainWindow;

        NativeMenu menu = new NativeMenu();

        NativeMenuItem showItem = new NativeMenuItem("Show VM Manager");
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        _statusMenuItem = new NativeMenuItem("Agent: starting...") { IsEnabled = false };
        menu.Add(_statusMenuItem);

        _sessionsMenuItem = new NativeMenuItem("Active RDP sessions: 0") { IsEnabled = false };
        menu.Add(_sessionsMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        NativeMenuItem quitItem = new NativeMenuItem("Quit Agent");
        quitItem.Click += (_, _) => RequestQuit();
        menu.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://VmManager/Assets/app.ico"))),
            ToolTipText = "VM Manager",
            Menu = menu,
            IsVisible = true,
        };

        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();

        _logger.LogInformation("Tray icon initialized");
    }

    public void Show()
    {
        if (_trayIcon == null)
            return;
        _trayIcon.IsVisible = true;
        _ = RefreshStatusAsync();
    }

    public void Hide()
    {
        if (_trayIcon == null)
            return;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
            return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
    }

    private async void RequestQuit()
    {
        _logger.LogInformation("Quit Agent requested from tray");

        int activeSessions = 0;
        try
        {
            AgentClient? client = App.AgentClient;
            if (client != null)
            {
                using CancellationTokenSource cts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(3)
                );
                activeSessions = await client.GetActiveRdpSessionCountAsync().WaitAsync(cts.Token);
            }
        }
        catch { }

        // TODO: show confirmation dialog if activeSessions > 0

        App.IsShuttingDown = true;

        try
        {
            _trayIcon?.Dispose();
        }
        catch { }

        Serilog.Log.CloseAndFlush();
        Environment.Exit(0);
    }

    private async Task RefreshStatusAsync()
    {
        if (_trayIcon == null || _statusMenuItem == null || _sessionsMenuItem == null)
            return;

        AgentClient? client = App.AgentClient;
        if (client == null)
        {
            _statusMenuItem.Header = "Agent: not connected";
            _sessionsMenuItem.Header = "Active RDP sessions: -";
            return;
        }

        try
        {
            int count = await client.GetActiveRdpSessionCountAsync();
            string url = client.BaseUrl;
            _statusMenuItem.Header = "Agent: " + url;
            _sessionsMenuItem.Header = "Active RDP sessions: " + count;
            _trayIcon.ToolTipText = "VM Manager - " + count + " RDP session(s)";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tray status refresh failed");
            _statusMenuItem.Header = "Agent: unreachable";
            _sessionsMenuItem.Header = "Active RDP sessions: -";
        }
    }

    public void Dispose()
    {
        _statusTimer?.Stop();
        _statusTimer = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
