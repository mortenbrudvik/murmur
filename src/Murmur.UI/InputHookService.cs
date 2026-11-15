using Mediator;
using Murmur.Common;
using Murmur.Core.Dictation;
using Murmur.Core.Features.App;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Services;
using Murmur.UI.Tray;

namespace Murmur.UI;

/// <summary>
/// The keyboard hook for as long as Murmur runs. If it cannot be installed, Murmur keeps running (history, settings) but
/// the hotkey cannot work, so it says so: the tray tooltip and a notification, and the main window's alert (through
/// <see cref="InputStatus"/>, and a push for a window that asked before the second attempt ended).
/// </summary>
public sealed class InputHookService(
    HotkeyMatcher matcher, HotkeyCaptureCoordinator coordinator, IKeyboardSender keyboard, TimeProvider time,
    DictationController controller, InputStatus input, IMediator mediator, TrayIconHost tray, AppLifecycle lifecycle)
    : IDisposable
{
    private InputHookHost? _hooks;
    private bool _disposed;

    /// <summary>
    /// Starts the hook, which <see cref="InputHookStarter"/> tries twice without holding up the UI thread. Call once, on
    /// the UI thread: hook signals reach the dictation controller through its dispatcher.
    /// </summary>
    public async Task StartAsync()
    {
        var dispatcher = tray.Dispatcher;
        var starter = new InputHookStarter(
            () => new InputHookHost(
                matcher, coordinator, keyboard, time,
                (signal, at) => dispatcher.BeginInvoke(() => controller.HandleSignal(signal, at))),
            time);

        // Resumes on the UI thread, where Quit (and so Dispose) also runs.
        var hooks = await starter.StartAsync();
        if (_disposed || lifecycle.IsQuitting)
        {
            // Murmur began to quit during the wait, after Dispose found no hooks to remove.
            hooks?.Dispose();
            return;
        }

        if (hooks is not null)
        {
            _hooks = hooks;
            return;
        }

        input.Error = starter.Failure?.Message;
        input.HookInstalled = false;

        tray.Refresh();
        // Persistent: the hotkey stays off for the whole run, so no later notice may push this one out.
        tray.ShowNotification(TrayNotifier.HotkeyUnavailable.Title, TrayNotifier.HotkeyUnavailable.Message, persistent: true);
        mediator.Publish(new InputStatusChangedNotification(input)).FireAndForget();
    }

    /// <summary>Removes the hook. Quit calls it before the container goes, which calls it again.</summary>
    public void Dispose()
    {
        _disposed = true;
        _hooks?.Dispose();
        _hooks = null;
    }
}
