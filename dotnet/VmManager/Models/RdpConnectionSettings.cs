namespace VmManager.Models;

public class RdpConnectionSettings
{
    public bool Fullscreen { get; set; } = true;
    public int DesktopWidth { get; set; } = 1920;
    public int DesktopHeight { get; set; } = 1080;
    public bool UseMultiMon { get; set; }
    public AudioPlaybackMode AudioMode { get; set; } = AudioPlaybackMode.Local;
    public bool RedirectClipboard { get; set; } = true;

    public int SessionBpp { get; set; } = 32;
    public bool SmartSizing { get; set; }
    public bool DynamicResolution { get; set; } = true;
    public bool AudioCapture { get; set; }
    public string DrivesToRedirect { get; set; } = "";
    public bool RedirectPrinters { get; set; }
    public KeyboardHookMode KeyboardHook { get; set; } = KeyboardHookMode.FullscreenOnly;
    public bool AllowFontSmoothing { get; set; } = true;
    public bool ShowWallpaper { get; set; } = true;
    public bool RedirectCameras { get; set; }
    public bool RedirectUsb { get; set; }
    public NetworkConnectionType ConnectionType { get; set; } = NetworkConnectionType.AutoDetect;
}

public enum AudioPlaybackMode
{
    Local = 0,
    Remote = 1,
    Off = 2,
}

public enum KeyboardHookMode
{
    LocalOnly = 0,
    RemoteAlways = 1,
    FullscreenOnly = 2,
}

public enum NetworkConnectionType
{
    Modem = 1,
    LowBroadband = 2,
    Satellite = 3,
    HighBroadband = 4,
    Wan = 5,
    Lan = 6,
    AutoDetect = 7,
}
