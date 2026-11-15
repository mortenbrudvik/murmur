using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.UI.Native;
using Serilog;
using DrawingIcon = System.Drawing.Icon;

namespace Murmur.UI.Tray;

/// <summary>
/// Murmur's icon in the notification area: its home while the main window is hidden. Left click, or Enter or Space on
/// the focused icon (Win+B, then the arrow keys), raises <see cref="OpenRequested"/>. The menu has "Settings"
/// (<see cref="SettingsRequested"/>), "About" (<see cref="AboutRequested"/>) and "Quit" (<see cref="QuitRequested"/>);
/// it opens on a right click and from the keyboard (Shift+F10 or the menu key). The icon and tooltip follow the
/// speech engine: a gradient dot when it is ready, a grey dot while it downloads, loads or has failed.
/// <para>
/// Adding the icon fails while Explorer's taskbar is not ready yet, which is likely right after sign-in, exactly when
/// Murmur starts with Windows. That is never fatal: the icon is retried every few seconds for a while, and again
/// whenever Explorer announces a new taskbar (H.NotifyIcon's own TaskbarCreated handling). A notification asked for
/// meanwhile waits for the icon, however it arrives: by a retry, or by H.NotifyIcon adding it by itself.
/// </para>
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    /// <summary>The notification area's tooltip holds 128 characters, including the terminating null.</summary>
    internal const int MaxToolTipLength = 127;

    /// <summary>The hotkey can change in Settings at any time; hovering the icon re-reads it at most this often.</summary>
    private static readonly TimeSpan HotkeyRefreshInterval = TimeSpan.FromSeconds(2);

    // Assembly-qualified, so they resolve whichever assembly started the process.
    private const string ReadyIconUri = "pack://application:,,,/Murmur.UI;component/Assets/tray-ready.ico";
    private const string BusyIconUri = "pack://application:,,,/Murmur.UI;component/Assets/tray-busy.ico";

    private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(3);
    private const int DefaultMaxCreateAttempts = 10; // the first attempt and about 30 s of retries

    /// <summary>How long after the right button was used on the icon a request for the menu still belongs to that click.</summary>
    private const long RightClickWindowMilliseconds = 1000;

    private readonly ILogger _logger = Log.ForContext<TrayIconHost>();
    private readonly ISpeechEngine _engine;
    private readonly ISettingsService _settings;
    private readonly InputStatus _input;
    private readonly Action<TaskbarIcon> _create;
    private readonly Action<TaskbarIcon, string, string> _show;
    private readonly Func<TaskbarIcon, bool> _isCreated;
    private readonly TimeSpan _retryInterval;
    private readonly int _maxCreateAttempts;

    private TaskbarIcon? _icon;
    private bool _iconAdded;
    private Notice? _pendingNotice;
    private DispatcherTimer? _retryTimer;
    private int _createAttempts;
    private bool? _showingReadyIcon;
    private string _hotkeyName = HotkeyDefinition.Default.DisplayName;
    private DateTime _hotkeyReadAt = DateTime.MinValue;
    private bool _disposed;

    /// <summary>Where the menu goes when the keyboard opens it, in screen pixels; null when the mouse does.</summary>
    private Point? _keyboardMenuAnchor;

    /// <summary>When the right mouse button was last pressed or released on the icon; null until it has been.</summary>
    private long? _lastRightMouseAt;

    public TrayIconHost(ISpeechEngine engine, ISettingsService settings, InputStatus input)
        // Efficiency Mode (H.NotifyIcon's default) would run all of Murmur at idle priority, which delays the keyboard
        // hook and slows transcription.
        : this(engine, settings, input, static icon => icon.ForceCreate(enablesEfficiencyMode: false), DefaultRetryInterval, DefaultMaxCreateAttempts)
    {
    }

    /// <summary>
    /// Test seam: lets tests make adding the icon fail, retry quickly, say whether the icon exists without one being
    /// added (<paramref name="isCreated"/>; null asks the icon) and keep notifications off the screen
    /// (<paramref name="show"/>; null shows the real balloon).
    /// </summary>
    internal TrayIconHost(
        ISpeechEngine engine, ISettingsService settings, InputStatus input, Action<TaskbarIcon> create, TimeSpan retryInterval,
        int maxCreateAttempts, Action<TaskbarIcon, string, string>? show = null, Func<TaskbarIcon, bool>? isCreated = null)
    {
        _engine = engine;
        _settings = settings;
        _input = input;
        _create = create;
        _show = show ?? ShowBalloon;
        _isCreated = isCreated ?? IsCreated;
        _retryInterval = retryInterval;
        _maxCreateAttempts = maxCreateAttempts;
    }

    /// <summary>The icon itself was picked, or a notification clicked: show the main window as it is.</summary>
    public event Action? OpenRequested;

    /// <summary>"Settings" was picked: show the main window on its Settings page.</summary>
    public event Action? SettingsRequested;

    /// <summary>"About" was picked: show the main window with its About dialog open.</summary>
    public event Action? AboutRequested;

    public event Action? QuitRequested;

    /// <summary>
    /// Puts the icon in the notification area. Call once, on the UI thread. Never fails because the taskbar is not
    /// ready: the icon then appears as soon as a retry succeeds.
    /// </summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon is not null)
            return;

        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        var about = new MenuItem { Header = "About" };
        about.Click += (_, _) => AboutRequested?.Invoke();
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => QuitRequested?.Invoke();

        var menu = new ContextMenu { Items = { settings, about, new Separator(), quit } };

        _icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            ContextMenu = menu,
        };
        _icon.TrayLeftMouseUp += (_, _) => OpenRequested?.Invoke();
        // H.NotifyIcon only handles the mouse by itself; without these a keyboard-only user could neither open Murmur
        // from the icon nor reach Quit.
        _icon.TrayKeyboardSelect += (_, _) => OpenRequested?.Invoke();
        _icon.TrayKeyboardKeySelect += (_, _) => OpenRequested?.Invoke();
        _icon.TrayKeyboardContextMenu += (_, _) => ShowMenuFromKeyboard();
        // Both ends of a right click: see IsFromMouse.
        _icon.TrayRightMouseDown += (_, _) => _lastRightMouseAt = Environment.TickCount64;
        _icon.TrayRightMouseUp += (_, _) => _lastRightMouseAt = Environment.TickCount64;
        // Raised each time H.NotifyIcon has opened the menu or moved an open one.
        _icon.TrayContextMenuOpen += (_, _) => AnchorMenu(menu);
        _icon.TrayBalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        _icon.TrayMouseMove += (_, _) => Update(rereadHotkey: false);

        Update(rereadHotkey: true);
        _engine.StatusChanged += OnStatusChanged;
        TryCreate();
    }

    /// <summary>
    /// Shows a Windows notification (a balloon, which Windows 11 presents as a toast) through the icon; clicking it
    /// raises <see cref="OpenRequested"/>. Call on the UI thread. While the icon is not in the notification area yet
    /// (before <see cref="Show"/>, or while the taskbar is not ready) the notification waits and is shown as soon as the
    /// icon is there: right after a retry adds it, or at the next <see cref="Refresh"/> or status change when
    /// H.NotifyIcon added it by itself. Only one waits, the newest, so a notification shown at once also drops an older
    /// one that was waiting. Does nothing after <see cref="Dispose"/>. Never throws: a notification is not worth a crash.
    /// </summary>
    /// <param name="persistent">
    /// The notification is about a condition that lasts (the hotkey cannot work). While it waits for the icon it is not
    /// displaced by a later notification that is not persistent: that one is dropped, because it would only replace
    /// this one on the screen a moment after it appeared, and its news is in the window and the tooltip as well.
    /// </param>
    public void ShowNotification(string title, string message, bool persistent = false)
    {
        if (_disposed)
        {
            _logger.Debug("No tray icon to show the notification \"{Title}\" with", title);
            return;
        }

        if (_pendingNotice is { Persistent: true } && !persistent)
        {
            _logger.Debug("The notification \"{Title}\" is dropped: a notice about a lasting problem is still to be shown", title);
            FlushPendingNotice();
            return;
        }

        _pendingNotice = new Notice(title, message, persistent);
        FlushPendingNotice();
        if (_pendingNotice is not null)
            _logger.Debug("The tray icon is not there yet; the notification \"{Title}\" waits for it", title);
    }

    /// <summary>
    /// Re-reads the engine status and the hook state into the icon and tooltip, for when the hook state changes, and
    /// shows a waiting notification if the icon has appeared. Call on the UI thread. (Hovering the icon and every
    /// engine status change do the same.)
    /// </summary>
    public void Refresh() => Update(rereadHotkey: false);

    /// <summary>The icon is in the notification area: a retry added it, or H.NotifyIcon did when Explorer's taskbar came up.</summary>
    private bool IconIsThere => _icon is { } icon && (_iconAdded || _isCreated(icon));

    /// <summary>
    /// Shows the waiting notification if the icon is there. One that fails to show is logged and not kept: it would
    /// only fail again.
    /// </summary>
    private void FlushPendingNotice()
    {
        if (_pendingNotice is not { } notice || !IconIsThere)
            return;

        _pendingNotice = null;
        try
        {
            _show(_icon!, notice.Title, notice.Message);
            _logger.Information("Tray notification: {Title}", notice.Title);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not show the tray notification \"{Title}\"", notice.Title);
        }
    }

    private static bool IsCreated(TaskbarIcon icon) => icon.IsCreated;

    /// <summary>
    /// Moves the menu next to the icon as it opens: to the click, or to the notification area when the keyboard
    /// opened it. Where H.NotifyIcon and WPF put it by themselves is wrong when monitors are scaled differently (see
    /// <see cref="TrayMenuPlacement"/>).
    /// </summary>
    private void AnchorMenu(ContextMenu menu)
    {
        var anchor = _keyboardMenuAnchor ?? TrayMenuNative.CursorPosition();
        _keyboardMenuAnchor = null;
        if (anchor is not { } point)
            return;

        PlaceMenu(menu, point, report: false);

        // Once more when the menu has been laid out: WPF places a popup again when its size settles, or when the move
        // took it to a monitor with another scale.
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PlaceMenu(menu, point, report: true));
    }

    /// <summary>Never throws: a menu in the wrong place is still a menu.</summary>
    private void PlaceMenu(ContextMenu menu, Point anchor, bool report)
    {
        try
        {
            if (!menu.IsOpen)
                return;

            var placed = TrayMenuPlacement.TryPlace(menu, anchor);
            if (!report)
                return;

            // Where it ended up is the first thing to know when it is reported in the wrong place on some arrangement
            // of monitors.
            var window = PresentationSource.FromVisual(menu) is System.Windows.Interop.HwndSource source
                ? TrayMenuNative.WindowBounds(source.Handle)
                : null;
            _logger.Debug("Tray menu opened for {Anchor}: placed {Placed}, its window is at {Window}", anchor, placed, window);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not move the tray menu next to the icon");
        }
    }

    /// <summary>
    /// Whether a request for the menu comes from a right click that H.NotifyIcon answers by itself. Windows sends the
    /// "context menu" notification for a right click as well as for Shift+F10 or the menu key on the focused icon,
    /// and nothing in it says which it was; what tells them apart is that the right button was used on the icon a
    /// moment before. Times are <see cref="Environment.TickCount64"/> values.
    /// </summary>
    internal static bool IsFromMouse(long? lastRightMouseAt, long now) =>
        lastRightMouseAt is { } at && now - at < RightClickWindowMilliseconds;

    /// <summary>
    /// Opens the menu at the notification area, where the keyboard focus is, rather than at the mouse. Not after a
    /// right click: that menu is opened by H.NotifyIcon, at the click.
    /// </summary>
    private void ShowMenuFromKeyboard()
    {
        if (_icon is null || IsFromMouse(_lastRightMouseAt, Environment.TickCount64))
            return;

        try
        {
            _keyboardMenuAnchor = TrayMenuNative.NotificationAreaCorner();
            _icon.ShowContextMenu(TrayInfo.GetTrayLocation(0));
        }
        catch (Exception ex)
        {
            _keyboardMenuAnchor = null;
            _logger.Warning(ex, "Could not open the tray menu from the keyboard");
        }
    }

    private static void ShowBalloon(TaskbarIcon icon, string title, string message) =>
        icon.ShowNotification(title, message, NotificationIcon.Info);

    /// <summary>The tooltip currently shown, or null before <see cref="Show"/>.</summary>
    internal string? ToolTipText => _icon?.ToolTipText;

    /// <summary>The icon's menu, or null before <see cref="Show"/>.</summary>
    internal ContextMenu? Menu => _icon?.ContextMenu;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _retryTimer?.Stop();
        _engine.StatusChanged -= OnStatusChanged;
        _icon?.Dispose();
    }

    /// <summary>
    /// The tooltip for an engine state; long error messages are cut to fit. Ready stays Ready while a model switch
    /// downloads the next model: the current one keeps dictating meanwhile, and the download shows in Settings. The
    /// load of the next model is not "loading model…" either, for the same reason. Without the keyboard hook, whenever
    /// the engine could dictate (Ready, or the load of a switch) the tooltip says the hotkey is not working instead.
    /// </summary>
    internal static string FormatToolTip(EngineStatus status, string hotkeyName, bool hookInstalled = true)
    {
        var text = status.State switch
        {
            _ when !hookInstalled && ShowsReadyIcon(status) => "Murmur — the hotkey isn't working",
            _ when status.Switching => $"Murmur — switching to {status.ModelName}…",
            EngineState.Ready => $"Murmur — hold {hotkeyName} to talk",
            EngineState.Downloading when status.Download is { Reconnecting: true } => "Murmur — reconnecting to download the speech model…",
            EngineState.Downloading => $"Murmur — downloading speech model {WholeFraction(status.Download):P0}",
            EngineState.Error => $"Murmur — {status.Error ?? "the speech engine failed"}",
            _ => "Murmur — loading model…",
        };

        return text.Length <= MaxToolTipLength ? text : string.Concat(text.AsSpan(0, MaxToolTipLength - 1), "…");
    }

    /// <summary>
    /// The gradient dot: the engine can dictate, which includes the load of a switched-to model (its status is Loading,
    /// but the previous model is still there to dictate with).
    /// </summary>
    internal static bool ShowsReadyIcon(EngineStatus status) => status.State == EngineState.Ready || status.Switching;

    /// <summary>The share downloaded, cut to a whole percent so the tooltip never shows 100% before the end.</summary>
    private static double WholeFraction(DownloadProgress? download)
    {
        if (download is not { TotalBytes: > 0 })
            return 0;

        // In whole numbers: 0.29 * 100 is 28.999… in floating point.
        return Math.Clamp(download.BytesReceived, 0, download.TotalBytes) * 100 / download.TotalBytes / 100.0;
    }

    /// <summary>Adds the icon to the notification area; on failure, schedules the next attempt while any are left.</summary>
    private void TryCreate()
    {
        if (_disposed || _icon is null)
            return;

        _createAttempts++;
        try
        {
            _create(_icon);
        }
        catch (Exception ex)
        {
            if (_createAttempts < _maxCreateAttempts)
            {
                if (_createAttempts == 1)
                    _logger.Warning(ex, "Could not add the tray icon (the taskbar may not be ready yet); retrying every {Interval}", _retryInterval);

                _retryTimer ??= CreateRetryTimer();
                _retryTimer.Start();
            }
            else
            {
                _logger.Warning(ex, "Could not add the tray icon after {Attempts} attempts; it appears when Explorer next announces the taskbar", _createAttempts);
                _retryTimer?.Stop();
            }

            return;
        }

        _iconAdded = true;
        _retryTimer?.Stop();
        if (_createAttempts > 1)
            _logger.Information("Tray icon added on attempt {Attempt}", _createAttempts);

        FlushPendingNotice();
    }

    private DispatcherTimer CreateRetryTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _icon!.Dispatcher) { Interval = _retryInterval };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryCreate();
        };
        return timer;
    }

    /// <summary>Raised on whichever thread changed the status.</summary>
    private void OnStatusChanged(EngineStatus status) =>
        _icon?.Dispatcher.BeginInvoke(() => Update(rereadHotkey: true));

    private void Update(bool rereadHotkey)
    {
        if (_disposed || _icon is null)
            return;

        FlushPendingNotice();

        var status = _engine.Status;
        if (status.State == EngineState.Ready && (rereadHotkey || DateTime.UtcNow - _hotkeyReadAt >= HotkeyRefreshInterval))
        {
            _hotkeyReadAt = DateTime.UtcNow;
            _hotkeyName = _settings.ReadHotkeyName(fallback: _hotkeyName); // unreadable settings keep the last name
        }

        var ready = ShowsReadyIcon(status);
        if (_showingReadyIcon != ready)
        {
            // The TaskbarIcon owns the icon it is given and disposes it when it is replaced, so it gets a fresh one
            // every time.
            _icon.Icon = LoadIcon(ready ? ReadyIconUri : BusyIconUri);
            _showingReadyIcon = ready;
        }

        var toolTip = FormatToolTip(status, _hotkeyName, _input.HookInstalled);
        if (toolTip != _icon.ToolTipText)
        {
            _icon.ToolTipText = toolTip;
            _logger.Debug("Tray tooltip: {ToolTip}", toolTip);
        }
    }

    /// <summary>Loads the frame that matches the notification area's icon size at the current DPI.</summary>
    private static DrawingIcon LoadIcon(string packUri)
    {
        // Reading the scheme registers "pack:" with Uri; a running Application has done so already, tests have not.
        _ = PackUriHelper.UriSchemePack;
        var uri = new Uri(packUri, UriKind.Absolute);
        var resource = Application.GetResourceStream(uri)
            ?? throw new InvalidOperationException($"Missing tray icon resource {uri}.");
        using var stream = resource.Stream;
        var size = User32.GetSystemMetrics(User32.SM_CXSMICON);
        return new DrawingIcon(stream, size, size);
    }

    /// <summary>A notification that waits for the icon.</summary>
    private readonly record struct Notice(string Title, string Message, bool Persistent);
}
