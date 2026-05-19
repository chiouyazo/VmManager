namespace VmManager.Services;

// TODO: Implement native OS notifications (Windows toast, macOS notification center, Linux notify-send)
// Avalonia has no built-in support. Options:
//   - DesktopNotifications NuGet package (cross-platform)
//   - Microsoft.Toolkit.Uwp.Notifications (Windows-only, proper toast)
//   - Manual platform-specific implementation
//
// Should fire for long-running background tasks:
//   - VM creation complete/failed (ImagesViewModel)
//   - Image import complete/failed (ImagesViewModel)
//   - Snapshot clone complete (MyVmsViewModel)
//   - Snapshot push complete (MyVmsViewModel)

public class NativeNotificationService
{
    public void Show(string title, string message) { }
}
