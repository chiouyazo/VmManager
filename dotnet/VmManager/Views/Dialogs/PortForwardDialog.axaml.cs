using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VmManager.Views.Dialogs;

public partial class PortForwardDialog : Window
{
    public int RemotePort { get; private set; }
    public int LocalPort { get; private set; }

    public PortForwardDialog()
    {
        InitializeComponent();
    }

    private void Forward_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RemotePortBox.Text, out int remote) || remote < 1 || remote > 65535)
            return;

        int local = remote;
        if (!string.IsNullOrWhiteSpace(LocalPortBox.Text))
        {
            if (!int.TryParse(LocalPortBox.Text, out local) || local < 1 || local > 65535)
                return;
        }

        RemotePort = remote;
        LocalPort = local;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
