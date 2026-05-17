using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VmManager.Contracts.Models;

namespace VmManager.Views.Dialogs;

public partial class ShareVmDialog : Window
{
    private readonly string _vmName;
    private List<VmShareEntry> _shares = [];
    private VmShareEntry? _editingShare;

    public ShareVmDialog(string vmName)
    {
        _vmName = vmName;
        InitializeComponent();
        Title = "Share VM: " + vmName;
        _ = LoadSharesAsync();
    }

    private async Task LoadSharesAsync()
    {
        if (App.AgentClient == null)
            return;

        try
        {
            _shares = await App.AgentClient.GetVmSharesAsync(_vmName);
            RebuildSharesList();
        }
        catch (Exception ex)
        {
            SharesList.Children.Clear();
            SharesList.Children.Add(
                new TextBlock
                {
                    Text = "Failed to load shares: " + ex.Message,
                    Foreground = Brushes.Red,
                }
            );
        }
    }

    private void RebuildSharesList()
    {
        SharesList.Children.Clear();

        if (_shares.Count == 0)
        {
            SharesList.Children.Add(
                new TextBlock
                {
                    Text = "Not shared with anyone yet.",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 4),
                }
            );
            return;
        }

        foreach (VmShareEntry share in _shares)
        {
            Border row = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = Brushes.LightGray,
                Padding = new Thickness(0, 6),
            };

            DockPanel dock = new DockPanel();

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                [DockPanel.DockProperty] = Dock.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Button editBtn = new Button
            {
                Content = "Edit",
                Padding = new Thickness(8, 4),
                FontSize = 11,
                Margin = new Thickness(0, 0, 4, 0),
                Tag = share,
            };
            editBtn.Click += EditShare_Click;
            buttons.Children.Add(editBtn);

            Button removeBtn = new Button
            {
                Content = "Remove",
                Padding = new Thickness(8, 4),
                FontSize = 11,
                Background = new SolidColorBrush(Color.Parse("#C8102E")),
                Foreground = Brushes.White,
                Tag = share.SharedWithUsername,
            };
            removeBtn.Click += RemoveShare_Click;
            buttons.Children.Add(removeBtn);

            dock.Children.Add(buttons);

            StackPanel info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(
                new TextBlock
                {
                    Text = share.SharedWithUsername,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 13,
                }
            );
            info.Children.Add(
                new TextBlock
                {
                    Text = string.Join(", ", share.GrantedPermissions.Select(FormatPermission)),
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                }
            );
            dock.Children.Add(info);

            row.Child = dock;
            SharesList.Children.Add(row);
        }
    }

    private void EditShare_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not VmShareEntry share)
            return;

        _editingShare = share;
        ShareUsernameBox.Text = share.SharedWithUsername;
        ShareUsernameBox.IsEnabled = false;
        AddShareButton.Content = "Save";

        ChkStart.IsChecked = share.GrantedPermissions.Contains(Permission.VmStart);
        ChkStop.IsChecked = share.GrantedPermissions.Contains(Permission.VmStop);
        ChkReset.IsChecked = share.GrantedPermissions.Contains(Permission.VmReset);
        ChkRdp.IsChecked = share.GrantedPermissions.Contains(Permission.RdpConnect);
        ChkSnapshot.IsChecked = share.GrantedPermissions.Contains(Permission.SnapshotCreate);
        ChkRestore.IsChecked = share.GrantedPermissions.Contains(Permission.SnapshotRestore);
    }

    private async void AddShare_Click(object? sender, RoutedEventArgs e)
    {
        if (App.AgentClient == null)
            return;

        string username = ShareUsernameBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(username))
            return;

        HashSet<string> permissions = CollectPermissions();

        try
        {
            await App.AgentClient.ShareVmAsync(_vmName, username, permissions);
            ResetAddForm();
            await LoadSharesAsync();
        }
        catch (Exception ex)
        {
            SharesList.Children.Add(
                new TextBlock
                {
                    Text = "Error: " + ex.Message,
                    Foreground = Brushes.Red,
                    Margin = new Thickness(0, 4),
                }
            );
        }
    }

    private async void RemoveShare_Click(object? sender, RoutedEventArgs e)
    {
        if (
            App.AgentClient == null
            || sender is not Button button
            || button.Tag is not string username
        )
            return;

        try
        {
            await App.AgentClient.UnshareVmAsync(_vmName, username);
            await LoadSharesAsync();
        }
        catch { }
    }

    private void ResetAddForm()
    {
        _editingShare = null;
        ShareUsernameBox.Text = "";
        ShareUsernameBox.IsEnabled = true;
        AddShareButton.Content = "Share";
        ChkStart.IsChecked = true;
        ChkStop.IsChecked = true;
        ChkReset.IsChecked = false;
        ChkRdp.IsChecked = true;
        ChkSnapshot.IsChecked = false;
        ChkRestore.IsChecked = false;
    }

    private HashSet<string> CollectPermissions()
    {
        HashSet<string> permissions = [];
        if (ChkStart.IsChecked == true)
            permissions.Add(Permission.VmStart);
        if (ChkStop.IsChecked == true)
            permissions.Add(Permission.VmStop);
        if (ChkReset.IsChecked == true)
            permissions.Add(Permission.VmReset);
        if (ChkRdp.IsChecked == true)
            permissions.Add(Permission.RdpConnect);
        if (ChkSnapshot.IsChecked == true)
            permissions.Add(Permission.SnapshotCreate);
        if (ChkRestore.IsChecked == true)
            permissions.Add(Permission.SnapshotRestore);
        return permissions;
    }

    private static string FormatPermission(string p)
    {
        return p switch
        {
            Permission.VmStart => "Start",
            Permission.VmStop => "Stop",
            Permission.VmReset => "Reset",
            Permission.RdpConnect => "RDP",
            Permission.SnapshotCreate => "Snapshot",
            Permission.SnapshotRestore => "Restore",
            _ => p,
        };
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
