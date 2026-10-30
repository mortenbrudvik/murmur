using System.IO;
using System.IO.Packaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Serilog;
using DrawingIcon = System.Drawing.Icon;

namespace Murmur.UI.Tray;

/// <summary>
/// Murmur's icon in the notification area: its home while the main window is hidden. Left click or "Open Murmur"
/// raises <see cref="OpenRequested"/>, "Quit" raises <see cref="QuitRequested"/>. The icon and tooltip follow the
/// speech engine: a gradient dot when it is ready, a grey dot while it downloads, loads or has failed.
/// <para>
/// Adding the icon fails while Explorer's taskbar is not ready yet, which is likely right after sign-in, exactly when
/// Murmur starts with Windows. That is never fatal: the icon is retried every few seconds for a while, and again
/// whenever Explorer announces a new taskbar (H.NotifyIcon's own TaskbarCreated handling). A notification asked for
/// meanwhile waits for the icon.
/// </para>
/// </summary>
public sealed partial class TrayIconHost : IDisposable
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

    private readonly ILogger _logger = Log.ForContext<TrayIconHost>();
    private readonly ISpeechEngine _engine;
    private readonly ISettingsService _settings;
    private readonly InputStatus _input;
    private readonly Action<TaskbarIcon> _create;
    private readonly Func<TaskbarIcon, string, string, bool> _show;
    private readonly TimeSpan _retryInterval;
    private readonly int _maxCreateAttempts;

    private TaskbarIcon? _icon;
    private bool _iconAdded;
    private (string Title, string Message)? _pendingNotice;
    private DispatcherTimer? _retryTimer;
    private int _createAttempts;
    private bool? _showingReadyIcon;
    private string _hotkeyName = HotkeyDefinition.Default.DisplayName;
    private DateTime _hotkeyReadAt = DateTime.MinValue;
    private bool _disposed;

    public TrayIconHost(ISpeechEngine engine, ISettingsService settings, InputStatus input)
        // Efficiency Mode (H.NotifyIcon's default) would run all of Murmur at idle priority, which delays the keyboard
        // hook and slows transcription.
        : this(engine, settings, input, static icon => icon.ForceCreate(enablesEfficiencyMode: false), DefaultRetryInterval, DefaultMaxCreateAttempts)
    {
    }

    /// <summary>
    /// Test seam: lets tests make adding the icon fail, retry quickly and keep notifications off the screen
    /// (<paramref name="show"/> shows one and says whether it could; null shows the real balloon).
    /// </summary>
    internal TrayIconHost(
        ISpeechEngine engine, ISettingsService settings, InputStatus input, Action<TaskbarIcon> create, TimeSpan retryInterval,
        int maxCreateAttempts, Func<TaskbarIcon, string, string, bool>? show = null)
    {
        _engine = engine;
        _settings = settings;
        _input = input;
        _create = create;
        _show = show ?? ShowBalloon;
        _retryInterval = retryInterval;
        _maxCreateAttempts = maxCreateAttempts;
    }

    public event Action? OpenRequested;

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

        var open = new MenuItem { Header = "Open Murmur" };
        open.Click += (_, _) => OpenRequested?.Invoke();
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => QuitRequested?.Invoke();

        _icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            ContextMenu = new ContextMenu { Items = { open, new Separator(), quit } },
        };
        _icon.TrayLeftMouseUp += (_, _) => OpenRequested?.Invoke();
        _icon.TrayBalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        _icon.TrayMouseMove += (_, _) => Update(rereadHotkey: false);

        Update(rereadHotkey: true);
        _engine.StatusChanged += OnStatusChanged;
        TryCreate();
    }

    /// <summary>
    /// Shows a Windows notification (a balloon, which Windows 10 and 11 present as a toast) through the icon; clicking it
    /// raises <see cref="OpenRequested"/>. Call on the UI thread. While the icon is not in the notification area yet
    /// (before <see cref="Show"/>, or while the taskbar is not ready) the notification waits and is shown right after the
    /// icon is added; only the newest one waits. Does nothing after <see cref="Dispose"/>. Never throws: a notification
    /// is not worth a crash.
    /// </summary>
    public void ShowNotification(string title, string message)
    {
        if (_disposed)
        {
            _logger.Debug("No tray icon to show the notification \"{Title}\" with", title);
            return;
        }

        if (!TryShow(title, message))
        {
            _pendingNotice = (title, message);
            _logger.Debug("The tray icon is not there yet; the notification \"{Title}\" waits for it", title);
        }
    }

    /// <summary>
    /// Re-reads the engine status and the hook state into the icon and tooltip, for when the hook state changes. Call on
    /// the UI thread. (Hovering the icon refreshes the tooltip as well.)
    /// </summary>
    public void Refresh() => Update(rereadHotkey: false);

    /// <summary>
    /// Shows the notification if the icon is in the notification area. Returns false if it is not, and so the
    /// notification is still to be shown; one that fails to show is logged and counts as shown.
    /// </summary>
    private bool TryShow(string title, string message)
    {
        if (_icon is not { } icon || !_iconAdded)
            return false;

        try
        {
            if (!_show(icon, title, message))
                return false;

            _logger.Information("Tray notification: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not show the tray notification \"{Title}\"", title);
        }

        return true;
    }

    private static bool ShowBalloon(TaskbarIcon icon, string title, string message)
    {
        if (!icon.IsCreated)
            return false;

        icon.ShowNotification(title, message, NotificationIcon.Info);
        return true;
    }

    /// <summary>The tooltip currently shown, or null before <see cref="Show"/>.</summary>
    internal string? ToolTipText => _icon?.ToolTipText;

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
    /// load of the next model is not "loading model…" either, for the same reason. Without the keyboard hook, the one
    /// line that advertises the hotkey says it is not working instead.
    /// </summary>
    internal static string FormatToolTip(EngineStatus status, string hotkeyName, bool hookInstalled = true)
    {
        var text = status.State switch
        {
            _ when status.Switching => $"Murmur — switching to {status.ModelName}…",
            EngineState.Ready when !hookInstalled => "Murmur — the hotkey isn't working",
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

        ShowPendingNotice();
    }

    /// <summary>Shows the notification that was asked for before the icon was added, once.</summary>
    private void ShowPendingNotice()
    {
        if (_pendingNotice is not var (title, message))
            return;

        _pendingNotice = null;
        if (!TryShow(title, message))
            _pendingNotice = (title, message);
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

        var status = _engine.Status;
        if (status.State == EngineState.Ready && (rereadHotkey || DateTime.UtcNow - _hotkeyReadAt >= HotkeyRefreshInterval))
            _hotkeyName = ReadHotkeyName();

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

    private string ReadHotkeyName()
    {
        _hotkeyReadAt = DateTime.UtcNow;
        try
        {
            return _settings.Load().Hotkey.DisplayName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "Could not read the hotkey for the tray tooltip");
            return _hotkeyName;
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
        var size = GetSystemMetrics(SM_CXSMICON);
        return new DrawingIcon(stream, size, size);
    }

    private const int SM_CXSMICON = 49;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);
}
