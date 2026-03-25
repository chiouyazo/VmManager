using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VmManager.Services;
using VmManager.ViewModels;
using VmManager.Views;
using VmManager.Views.Dialogs;
using VmManager.Views.Pages;

namespace VmManager;

/// <summary>
/// Application entry point. Configures the <see cref="IHost"/> with all services,
/// ViewModels and pages, then shows the main window.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(
                (_, services) =>
                {
                    // Application services
                    services.AddSingleton<BusyService>();
                    services.AddSingleton<SettingsService>();
                    services.AddSingleton<OciCatalogService>();
                    services.AddSingleton<NexusCatalogService>();
                    services.AddSingleton<CatalogService>();
                    services.AddSingleton<HyperVService>();
                    services.AddSingleton<DockerService>();
                    services.AddSingleton<VmBackendFactory>();
                    services.AddSingleton<IVmBackend>(sp => sp.GetRequiredService<HyperVService>());
                    services.AddSingleton<ImportService>();
                    services.AddSingleton<PreflightService>();

                    // ViewModels
                    services.AddTransient<ImagesViewModel>();
                    services.AddTransient<MyVmsViewModel>();
                    services.AddTransient<SnapshotsViewModel>();

                    // Pages (singletons so state is preserved between navigations)
                    services.AddSingleton<ImagesPage>();
                    services.AddSingleton<MyVmsPage>();
                    services.AddSingleton<SnapshotsPage>();
                    services.AddSingleton<SettingsPage>();

                    // Main window
                    services.AddSingleton<MainWindow>();
                }
            )
            .Build();

        await _host.StartAsync();

        // Self-elevate: restart as admin if not already elevated
        if (!PreflightService.IsRunningAsAdmin())
        {
            try
            {
                var exePath = Environment.ProcessPath!;
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas",
                    }
                );
            }
            catch
            {
                // User declined UAC prompt
            }

            Shutdown();
            return;
        }

        // Pre-flight: verify backends
        var preflight = _host.Services.GetRequiredService<PreflightService>();
        var hyperVOk = await preflight.IsHyperVAvailableAsync();
        var dockerOk = await preflight.IsDockerAvailableAsync();
        if (!hyperVOk && !dockerOk)
        {
            var result = MessageBox.Show(
                "Neither Hyper-V nor Docker is available on this machine.\n\n"
                    + "Enable Hyper-V in Windows Features or install Docker, then restart.\n\n"
                    + "Continue anyway?",
                "No VM Backend Found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (result == MessageBoxResult.No)
            {
                Shutdown();
                return;
            }
        }

        // First-run: show setup dialog if registry is not configured
        var settingsService = _host.Services.GetRequiredService<SettingsService>();
        var settings = settingsService.Load();
        if (!settings.IsRegistryConfigured)
        {
            var setup = new SetupDialog();
            if (setup.ShowDialog() == true && !setup.WasSkipped)
            {
                settings.RegistryUrl = setup.RegistryUrl;
                settings.RegistryRepository = setup.Repository;
                settingsService.Save(settings);
            }
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
