using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;

namespace Murmur.UI.Tray;

/// <summary>
/// Windows notifications through the tray icon for what the engine does while the window is not in front: the model is
/// ready after a download, the engine failed, the download has no internet. <see cref="TrayNotifier"/> decides what is
/// worth one; its state lives on the UI thread, where the icon is.
/// </summary>
public sealed class TrayEngineNotifications(
    ISpeechEngine engine, TrayIconHost tray, ISettingsService settings, InputStatus input, AppLifecycle lifecycle,
    MainWindow mainWindow)
{
    private readonly TrayNotifier _notifier = new();

    /// <summary>Starts following the engine. Call once.</summary>
    public void Start() => engine.StatusChanged += status => tray.Dispatcher.BeginInvoke(() => OnStatus(status));

    private void OnStatus(EngineStatus status)
    {
        if (lifecycle.IsQuitting)
            return;

        // Only a ready notice names the hotkey, so the settings are not read for every download progress update.
        var defaultName = HotkeyDefinition.Default.DisplayName;
        var hotkeyName = status.State == EngineState.Ready ? settings.ReadHotkeyName(fallback: defaultName) : defaultName;
        var notice = _notifier.OnStatus(status, mainWindow.IsActive, hotkeyName, input.HookInstalled);
        if (notice is not null)
            tray.ShowNotification(notice.Title, notice.Message);
    }
}
