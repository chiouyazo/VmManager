using System.Windows;
using VmManager.Services;

namespace VmManager.Views.Dialogs;

/// <summary>
/// First-run setup dialog that prompts for OCI registry connection details.
/// Auto-discovers repositories when the user enters a registry URL.
/// </summary>
public partial class SetupDialog : Window
{
    public string RegistryUrl => RegistryUrlBox.Text.Trim();
    public string Repository => RepositoryCombo.Text.Trim();
    public bool WasSkipped { get; private set; }

    public SetupDialog()
    {
        InitializeComponent();
        RegistryUrlBox.Focus();
    }

    private async void LoadRepos_Click(object sender, RoutedEventArgs e)
    {
        var url = NormalizeUrl(RegistryUrlBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText.Text = "Enter a registry URL first.";
            return;
        }

        RegistryUrlBox.Text = url;
        LoadReposButton.IsEnabled = false;
        StatusText.Text = "Connecting…";
        RepositoryCombo.Items.Clear();

        try
        {
            var repos = await OciCatalogService.ListRepositoriesAsync(url);

            if (repos.Count == 0)
            {
                StatusText.Text = "Connected, but no repositories found. Push an image first.";
                return;
            }

            foreach (var repo in repos)
                RepositoryCombo.Items.Add(repo);

            RepositoryCombo.SelectedIndex = 0;
            StatusText.Text = $"Found {repos.Count} repositor{(repos.Count == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to connect: {ex.Message}";
        }
        finally
        {
            LoadReposButton.IsEnabled = true;
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RegistryUrl))
        {
            StatusText.Text = "Please enter a Registry URL.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Repository))
        {
            StatusText.Text = "Please select or enter a Repository.";
            return;
        }

        // Normalize before saving
        RegistryUrlBox.Text = NormalizeUrl(RegistryUrlBox.Text.Trim());
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        WasSkipped = true;
        DialogResult = true;
    }

    /// <summary>Ensures the URL has a scheme (defaults to http for local IPs).</summary>
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
