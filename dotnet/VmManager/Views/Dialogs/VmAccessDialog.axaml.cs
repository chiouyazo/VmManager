using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VmManager.Contracts.Models;

namespace VmManager.Views.Dialogs;

public partial class VmAccessDialog : Window
{
    public string VmName { get; }
    public string OwnerName { get; }
    public ObservableCollection<GrantItem> Grants { get; } = [];
    public VmPermission[] PermissionOptions { get; } = Enum.GetValues<VmPermission>();

    private readonly Services.AgentClient _agentClient;

    public VmAccessDialog(Services.AgentClient agentClient, string vmName, VmAccessEntry entry)
    {
        _agentClient = agentClient;
        VmName = vmName;
        OwnerName = entry.Owner;
        foreach (VmAccessGrant g in entry.Grants)
            Grants.Add(new GrantItem { Username = g.Username, Permission = g.Permission });

        DataContext = this;
        InitializeComponent();

        NewPermissionBox.ItemsSource = PermissionOptions;
        NewPermissionBox.SelectedIndex = 0;
    }

    private void AddGrant_Click(object? sender, RoutedEventArgs e)
    {
        string username = NewUsernameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(username))
            return;
        if (Grants.Any(g => g.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            return;

        VmPermission perm = NewPermissionBox.SelectedItem is VmPermission p
            ? p
            : VmPermission.Connect;
        Grants.Add(new GrantItem { Username = username, Permission = perm });
        NewUsernameBox.Text = "";
    }

    private void RemoveGrant_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GrantItem grant)
            Grants.Remove(grant);
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            VmAccessEntry current = await _agentClient.GetVmAccessAsync(VmName);
            HashSet<string> newUsers = new(
                Grants.Select(g => g.Username),
                StringComparer.OrdinalIgnoreCase
            );

            foreach (VmAccessGrant existing in current.Grants)
            {
                if (!newUsers.Contains(existing.Username))
                    await _agentClient.RemoveVmAccessAsync(VmName, existing.Username);
            }

            foreach (GrantItem g in Grants)
                await _agentClient.SetVmAccessAsync(VmName, g.Username, g.Permission);

            Close(true);
        }
        catch
        {
            Close(false);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
