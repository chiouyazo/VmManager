using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using VmManager.Services;
using VmManager.ViewModels;
using VmManager.Views;
using VmManager.Views.Controls;
using VmManager.Views.Dialogs;
using VmManager.Views.Pages;

namespace VmManager;

public partial class App : Application
{
    private IHost? _host;
    private AgentConnection? _agentConnection;
    private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showWindowEvent;

    public static CancellationToken ShutdownToken { get; private set; } = CancellationToken.None;
    public static AgentClient? AgentClient { get; set; }
    public static AgentConnection? AgentConnection { get; private set; }
    public static bool IsShuttingDown { get; set; }

    public override void Initialize()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Fatal unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };

        try
        {
            await StartupAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            IsShuttingDown = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
    }

    private async Task StartupAsync()
    {
#if WINDOWS && !CLIENT_ONLY
        using (
            System.Security.Principal.WindowsIdentity identity =
                System.Security.Principal.WindowsIdentity.GetCurrent()
        )
        {
            System.Security.Principal.WindowsPrincipal principal =
                new System.Security.Principal.WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(
                System.Security.Principal.WindowsBuiltInRole.Administrator
            );

            if (
                !isAdmin
                && !Debugger.IsAttached
                && Environment.GetEnvironmentVariable("VMMANAGER_NO_ELEVATE") == null
            )
            {
                try
                {
                    string exePath = Environment.ProcessPath!;
                    Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = exePath,
                            UseShellExecute = true,
                            Verb = "runas",
                        }
                    );
                }
                catch { }

                IsShuttingDown = true;
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                return;
            }
        }
#endif

#if WINDOWS
        string mutexName = "Global\\VmManager_SingleInstance";
#if CLIENT_ONLY
        mutexName = "Global\\VmManager_Client_SingleInstance";
#endif
        _instanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                EventWaitHandle.OpenExisting("Global\\VmManager_ShowWindow").Set();
            }
            catch { }

            IsShuttingDown = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime dt)
                dt.Shutdown();
            return;
        }

        _showWindowEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            "Global\\VmManager_ShowWindow"
        );
#endif

        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VmManager",
            "Logs",
            "vmmanager-.log"
        );

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        Log.Information("VM Manager starting");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .UseConsoleLifetime(opts => opts.SuppressStatusMessages = true)
            .ConfigureServices(
                (_, services) =>
                {
                    services.AddSingleton<IAppPaths, AppPaths>();
                    services.AddSingleton<BusyService>();
                    services.AddSingleton<BackgroundTaskManager>();
                    services.AddSingleton<IBackgroundTaskManager>(sp =>
                        sp.GetRequiredService<BackgroundTaskManager>()
                    );
                    services.AddSingleton<TempTracker>();
                    services.AddSingleton<ITempTracker>(sp => sp.GetRequiredService<TempTracker>());
                    services.AddSingleton<AgentSettingsService>();
                    services.AddSingleton<AgentConnection>();
                    services.AddSingleton<TrayIconService>();

                    services.AddTransient<ImagesViewModel>();
                    services.AddTransient<MyVmsViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddSingleton<MainWindowViewModel>();

                    services.AddSingleton<ImagesPage>();
                    services.AddSingleton<MyVmsPage>();
                    services.AddSingleton<SettingsPage>();
                    services.AddSingleton<TaskPanel>();

                    services.AddSingleton<MainWindow>();
                }
            )
            .Build();

        await _host.StartAsync();

        ShutdownToken = _shutdownCts.Token;

        _host.Services.GetRequiredService<TempTracker>().CleanupOrphans();

        _agentConnection = _host.Services.GetRequiredService<AgentConnection>();
        AgentConnection = _agentConnection;

#if WINDOWS && !CLIENT_ONLY
        await _agentConnection.ConnectLocalAsync();
#endif

        AgentSettingsService agentSettings =
            _host.Services.GetRequiredService<AgentSettingsService>();
        string? lastSelectedId = agentSettings.LoadSelectedAgentId();
        List<AgentConfiguration> agents = agentSettings.Load();
        AgentConfiguration? lastAgent =
            lastSelectedId != null ? agents.FirstOrDefault(a => a.Id == lastSelectedId) : null;

        if (lastAgent != null)
        {
            try
            {
                if (lastAgent.IsLocal)
                {
                    _agentConnection.SwitchToLocal();
                }
                else
                {
                    await _agentConnection.ConnectRemoteAsync(
                        lastAgent.Url,
                        lastAgent.Username,
                        lastAgent.Password,
                        lastAgent.RdpProxyHost
                    );
                }
                AgentClient = _agentConnection.Client;
                Log.Information("Connected to agent: {AgentName}", lastAgent.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to connect to {AgentName}", lastAgent.Name);
            }
        }

        if (AgentClient != null && _agentConnection.IsLocal)
        {
            AppSettings settings = await AgentClient.GetSettingsAsync();
            if (!settings.HasCompletedSetup)
            {
                MainWindow tempOwner = _host.Services.GetRequiredService<MainWindow>();
                SetupWizardWindow wizard = new SetupWizardWindow();
                bool? wizardResult = await wizard.ShowDialog<bool?>(tempOwner);
                if (wizardResult == true)
                {
                    AppSettings result = wizard.Result;
                    result.DefaultVmUsername = settings.DefaultVmUsername;
                    result.DefaultVmPassword = settings.DefaultVmPassword;
                    await AgentClient.SaveSettingsAsync(result);
                }
                else
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime dt)
                        dt.Shutdown();
                    return;
                }
            }
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktopLifetime.ShutdownRequested += OnShutdownRequested;

            MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktopLifetime.MainWindow = mainWindow;
            mainWindow.Show();

            TrayIconService trayIcon = _host.Services.GetRequiredService<TrayIconService>();
            trayIcon.Initialize(mainWindow);

            if (OperatingSystem.IsWindows() && _showWindowEvent != null)
            {
                _ = Task.Run(() =>
                {
                    WaitHandle[] handles = [_showWindowEvent, _shutdownCts.Token.WaitHandle];
                    while (WaitHandle.WaitAny(handles) == 0)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            mainWindow.Show();
                            if (mainWindow.WindowState == WindowState.Minimized)
                                mainWindow.WindowState = WindowState.Normal;
                            mainWindow.Activate();
                        });
                    }
                });
            }
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        IsShuttingDown = true;
        Log.Information("VM Manager shutting down. Cancelling active operations");

        _shutdownCts.Cancel();

        if (_host != null)
        {
            TrayIconService? trayIcon = _host.Services.GetService<TrayIconService>();
            trayIcon?.Dispose();

            _agentConnection?.Dispose();

            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        _showWindowEvent?.Dispose();
        if (_instanceMutex != null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch { }
            _instanceMutex.Dispose();
        }

        await Log.CloseAndFlushAsync();
    }
}
