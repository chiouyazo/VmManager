using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VmManager.Models;

namespace VmManager.Views.Dialogs;

public partial class ConnectionSettingsDialog : Window
{
    private static readonly List<ResolutionPreset> Resolutions =
    [
        new ResolutionPreset("1280 x 720", 1280, 720),
        new ResolutionPreset("1366 x 768", 1366, 768),
        new ResolutionPreset("1600 x 900", 1600, 900),
        new ResolutionPreset("1920 x 1080", 1920, 1080),
        new ResolutionPreset("2560 x 1440", 2560, 1440),
        new ResolutionPreset("3840 x 2160", 3840, 2160),
    ];

    public RdpConnectionSettings? Settings { get; private set; }
    public bool RememberAsDefault { get; private set; }

    public ConnectionSettingsDialog(string vmName, RdpConnectionSettings defaults)
    {
        InitializeComponent();
        Title = "Connect to: " + vmName;
        HeaderText.Text = "Connect to " + vmName;

        PopulateResolutions();
        ApplySettings(defaults);

        Opened += (_, _) => ConnectButton.Focus();
    }

    private void PopulateResolutions()
    {
        foreach (ResolutionPreset preset in Resolutions)
            ResolutionBox.Items.Add(new ComboBoxItem { Content = preset.Label, Tag = preset });

        ResolutionBox.Items.Add(new ComboBoxItem { Content = "Match local display", Tag = null });
    }

    private void ApplySettings(RdpConnectionSettings settings)
    {
        DisplayModeBox.SelectedIndex = settings.Fullscreen ? 1 : 0;

        int resIndex = FindResolutionIndex(settings.DesktopWidth, settings.DesktopHeight);
        ResolutionBox.SelectedIndex = resIndex >= 0 ? resIndex : ResolutionBox.Items.Count - 1;

        MultiMonBox.IsChecked = settings.UseMultiMon;

        AudioLocal.IsChecked = settings.AudioMode == AudioPlaybackMode.Local;
        AudioRemote.IsChecked = settings.AudioMode == AudioPlaybackMode.Remote;
        AudioOff.IsChecked = settings.AudioMode == AudioPlaybackMode.Off;

        ClipboardBox.IsChecked = settings.RedirectClipboard;

        ColorDepthBox.SelectedIndex = settings.SessionBpp switch
        {
            16 => 0,
            24 => 1,
            _ => 2,
        };

        SmartSizingBox.IsChecked = settings.SmartSizing;
        DynamicResizeBox.IsChecked = settings.DynamicResolution;
        MicrophoneBox.IsChecked = settings.AudioCapture;

        DrivesBox.SelectedIndex = settings.DrivesToRedirect switch
        {
            "*" => 1,
            "DynamicDrives" => 2,
            _ => 0,
        };

        PrintersBox.IsChecked = settings.RedirectPrinters;
        KeyboardHookBox.SelectedIndex = (int)settings.KeyboardHook;
        FontSmoothingBox.IsChecked = settings.AllowFontSmoothing;
        WallpaperBox.IsChecked = settings.ShowWallpaper;
        WebcamBox.IsChecked = settings.RedirectCameras;
        UsbBox.IsChecked = settings.RedirectUsb;
        NetworkHintBox.SelectedIndex = (int)settings.ConnectionType - 1;
    }

    private RdpConnectionSettings CollectSettings()
    {
        int width = 1920;
        int height = 1080;

        if (ResolutionBox.SelectedItem is ComboBoxItem item && item.Tag is ResolutionPreset preset)
        {
            width = preset.Width;
            height = preset.Height;
        }
        else
        {
            Avalonia.Platform.Screen? primary = Screens?.Primary;
            if (primary != null)
            {
                width = (int)(primary.Bounds.Width / primary.Scaling);
                height = (int)(primary.Bounds.Height / primary.Scaling);
            }
        }

        AudioPlaybackMode audioMode = AudioPlaybackMode.Local;
        if (AudioRemote.IsChecked == true)
            audioMode = AudioPlaybackMode.Remote;
        else if (AudioOff.IsChecked == true)
            audioMode = AudioPlaybackMode.Off;

        int bpp = ColorDepthBox.SelectedIndex switch
        {
            0 => 16,
            1 => 24,
            _ => 32,
        };

        string drives = DrivesBox.SelectedIndex switch
        {
            1 => "*",
            2 => "DynamicDrives",
            _ => "",
        };

        return new RdpConnectionSettings
        {
            Fullscreen = DisplayModeBox.SelectedIndex == 1,
            DesktopWidth = width,
            DesktopHeight = height,
            UseMultiMon = MultiMonBox.IsChecked == true,
            AudioMode = audioMode,
            RedirectClipboard = ClipboardBox.IsChecked == true,
            SessionBpp = bpp,
            SmartSizing = SmartSizingBox.IsChecked == true,
            DynamicResolution = DynamicResizeBox.IsChecked == true,
            AudioCapture = MicrophoneBox.IsChecked == true,
            DrivesToRedirect = drives,
            RedirectPrinters = PrintersBox.IsChecked == true,
            KeyboardHook = (KeyboardHookMode)KeyboardHookBox.SelectedIndex,
            AllowFontSmoothing = FontSmoothingBox.IsChecked == true,
            ShowWallpaper = WallpaperBox.IsChecked == true,
            RedirectCameras = WebcamBox.IsChecked == true,
            RedirectUsb = UsbBox.IsChecked == true,
            ConnectionType = (NetworkConnectionType)(NetworkHintBox.SelectedIndex + 1),
        };
    }

    private int FindResolutionIndex(int width, int height)
    {
        for (int i = 0; i < Resolutions.Count; i++)
        {
            if (Resolutions[i].Width == width && Resolutions[i].Height == height)
                return i;
        }
        return -1;
    }

    private void Connect_Click(object? sender, RoutedEventArgs e)
    {
        Settings = CollectSettings();
        RememberAsDefault = RememberBox.IsChecked == true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void AdvancedToggle_Click(object? sender, RoutedEventArgs e)
    {
        AdvancedPanel.IsVisible = !AdvancedPanel.IsVisible;
        AdvancedToggleText.Text = AdvancedPanel.IsVisible ? "\u25BE Advanced" : "\u25B8 Advanced";
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Connect_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private sealed record ResolutionPreset(string Label, int Width, int Height);
}
