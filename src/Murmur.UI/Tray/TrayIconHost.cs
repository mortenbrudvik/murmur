using System.IO;
using System.IO.Packaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
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
/// whenever Explorer announces a new taskbar (H.NotifyIcon's own TaskbarCreated handling).
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
    private readonly Action<TaskbarIcon> _create;
    private readonly TimeSpan _retryInterval;
    private readonly int _maxCreateAttempts;

    private TaskbarIcon? _icon;
    private DispatcherTimer? _retryTimer;
    private int _createAttempts;
    private bool? _showingReadyIcon;
    private string _hotkeyName = HotkeyDefinition.Default.DisplayName;
    private DateTime _hotkeyReadAt = DateTime.MinValue;
    private bool _disposed;

    public TrayIconHost(ISpeechEngine engine, ISettingsService settings)
        // Efficiency Mode (H.NotifyIcon's default) would run all of Murmur at idle priority, which delays the keyboard
        // hook and slows transcription.
        : this(engine, settings, static icon => icon.ForceCreate(enablesEfficiencyMode: false), DefaultRetryInterval, DefaultMaxCreateAttempts)
    {
    }

    /// <summary>Test seam: lets tests make adding the icon fail, and retry quickly.</summary>
    internal TrayIconHost(
        ISpeechEngine engine, ISettingsService settings, Action<TaskbarIcon> create, TimeSpan retryInterval, int maxCreateAttempts)
    {
        _engine = engine;
        _settings = settings;
        _create = create;
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
        _icon.TrayMouseMove += (_, _) => Update(rereadHotkey: false);

        Update(rereadHotkey: true);
        _engine.StatusChanged += OnStatusChanged;
        TryCreate();
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

    /// <summary>The tooltip for an engine state; long error messages are cut to fit.</summary>
    internal static string FormatToolTip(EngineStatus status, string hotkeyName)
    {
        var text = status.State switch
        {
            EngineState.Ready => $"Murmur — hold {hotkeyName} to talk",
            EngineState.Downloading => $"Murmur — downloading model {status.DownloadProgress ?? 0:P0}",
            EngineState.Error => $"Murmur — {status.Error ?? "the speech engine failed"}",
            _ => "Murmur — loading model…",
        };

        return text.Length <= MaxToolTipLength ? text : string.Concat(text.AsSpan(0, MaxToolTipLength - 1), "…");
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

        _retryTimer?.Stop();
        if (_createAttempts > 1)
            _logger.Information("Tray icon added on attempt {Attempt}", _createAttempts);
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
        var ready = status.State == EngineState.Ready;
        if (ready && (rereadHotkey || DateTime.UtcNow - _hotkeyReadAt >= HotkeyRefreshInterval))
            _hotkeyName = ReadHotkeyName();

        if (_showingReadyIcon != ready)
        {
            // The TaskbarIcon owns the icon it is given and disposes it when it is replaced, so it gets a fresh one
            // every time.
            _icon.Icon = LoadIcon(ready ? ReadyIconUri : BusyIconUri);
            _showingReadyIcon = ready;
        }

        var toolTip = FormatToolTip(status, _hotkeyName);
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
