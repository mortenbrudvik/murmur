using Murmur.Core.Dictation;

namespace Murmur.UI.Tray;

/// <summary>Shows dictation news as a Windows notification through the tray icon, from any thread.</summary>
public sealed class TrayUserNotifier(TrayIconHost tray) : IUserNotifier
{
    public void Notify(string title, string message)
    {
        // The tray icon is the UI thread's. ShowNotification never throws, and does nothing once the icon is disposed.
        _ = tray.Dispatcher.BeginInvoke(() => tray.ShowNotification(title, message));
    }
}
