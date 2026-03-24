using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using VmManager.Models;
using VmManager.Services;

namespace VmManager.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.Page
{
    private readonly SettingsService _settingsService;

    public SettingsPage(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        Loaded += OnFirstLoaded;
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;

        var settings = _settingsService.Load();
        RegistryUrlBox.Text = settings.RegistryUrl;
        RegistryRepoCombo.Text = settings.RegistryRepository;
        LocalCatalogPathBox.Text = settings.LocalCatalogPath;
        LocalVmPathTextBox.Text = settings.LocalVmPath;
        NexusUrlBox.Text = settings.NexusUrl;
        NexusUsernameBox.Text = settings.NexusUsername;
        NexusPasswordBox.Text = settings.NexusPassword;
        NexusRepoBox.Text = settings.NexusRepository;
        MemoryMbBox.Text = settings.DefaultMemoryMb.ToString();
        CpuCountBox.Text = settings.DefaultCpuCount.ToString();
        UsernameBox.Text = settings.DefaultVmUsername;
        PasswordBox.Text = settings.DefaultVmPassword;

        // Auto-load repos if URL is already configured
        if (!string.IsNullOrWhiteSpace(settings.RegistryUrl))
            _ = LoadRepositoriesAsync(settings.RegistryUrl, settings.RegistryRepository);
    }

    private async Task LoadRepositoriesAsync(string url, string? selectRepo = null)
    {
        url = NormalizeUrl(url);
        RegistryUrlBox.Text = url;
        LoadReposButton.IsEnabled = false;
        RepoStatusText.Text = "Connecting…";

        var currentText = RegistryRepoCombo.Text;
        RegistryRepoCombo.Items.Clear();

        try
        {
            var repos = await OciCatalogService.ListRepositoriesAsync(url);

            if (repos.Count == 0)
            {
                RepoStatusText.Text = "Connected, but no repositories found.";
                return;
            }

            foreach (var repo in repos)
                RegistryRepoCombo.Items.Add(repo);

            // Re-select the previously configured repo, or the first one
            var toSelect = selectRepo ?? currentText;
            if (!string.IsNullOrEmpty(toSelect) && repos.Contains(toSelect))
                RegistryRepoCombo.Text = toSelect;
            else
                RegistryRepoCombo.SelectedIndex = 0;

            RepoStatusText.Text =
                $"{repos.Count} repositor{(repos.Count == 1 ? "y" : "ies")} found.";
        }
        catch (Exception ex)
        {
            RepoStatusText.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            LoadReposButton.IsEnabled = true;
        }
    }

    private async void LoadRepos_Click(object sender, RoutedEventArgs e)
    {
        var url = RegistryUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            RepoStatusText.Text = "Enter a registry URL first.";
            return;
        }

        await LoadRepositoriesAsync(url);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new AppSettings
            {
                RegistryUrl = NormalizeUrl(RegistryUrlBox.Text.Trim()),
                RegistryRepository = RegistryRepoCombo.Text.Trim(),
                LocalCatalogPath = LocalCatalogPathBox.Text.Trim(),
                LocalVmPath = LocalVmPathTextBox.Text.Trim(),
                NexusUrl = NormalizeUrl(NexusUrlBox.Text.Trim()),
                NexusUsername = NexusUsernameBox.Text.Trim(),
                NexusPassword = NexusPasswordBox.Text,
                NexusRepository = NexusRepoBox.Text.Trim(),
                DefaultMemoryMb = int.TryParse(MemoryMbBox.Text, out var mb) ? mb : 4096,
                DefaultCpuCount = int.TryParse(CpuCountBox.Text, out var cpu) ? cpu : 4,
                DefaultVmUsername = UsernameBox.Text.Trim(),
                DefaultVmPassword = PasswordBox.Text,
            };

            await _settingsService.SaveAsync(settings);
            ShowStatus(true, "Settings saved.");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Failed to save settings: {ex.Message}");
        }
    }

    private void BrowseLocalCatalogPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Local Catalog Folder (contains catalog.json)",
            InitialDirectory = LocalCatalogPathBox.Text,
        };
        if (dialog.ShowDialog() == true)
            LocalCatalogPathBox.Text = dialog.FolderName;
    }

    private void BrowseLocalVmPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Local VMs Folder",
            InitialDirectory = LocalVmPathTextBox.Text,
        };

        if (dialog.ShowDialog() == true)
            LocalVmPathTextBox.Text = dialog.FolderName;
    }

    private void ShowStatus(bool success, string message)
    {
        StatusBar.Background = new SolidColorBrush(
            success ? Color.FromRgb(0x10, 0x7C, 0x10) : Color.FromRgb(0xC4, 0x2B, 0x1C)
        );
        StatusText.Text = message;
        StatusBar.Visibility = Visibility.Visible;
    }

    private async void CopyStatus_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(StatusText.Text))
            return;
        try
        {
            System.Windows.Clipboard.SetText(StatusText.Text);
            if (sender is System.Windows.Controls.Button btn)
            {
                btn.Content = "✓";
                await Task.Delay(1200);
                btn.Content = "📋";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Copy failed");
        }
    }

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;
        if (
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
            url = $"http://{url}";
        return url.TrimEnd('/');
    }
}
