using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
/// </summary>
public sealed partial class TrayIconHost(ISpeechEngine engine, ISettingsService settings) : IDisposable
{
    /// <summary>The notification area's tooltip holds 128 characters, including the terminating null.</summary>
    internal const int MaxToolTipLength = 127;

    /// <summary>The hotkey can change in Settings at any time; hovering the icon re-reads it at most this often.</summary>
    private static readonly TimeSpan HotkeyRefreshInterval = TimeSpan.FromSeconds(2);

    // Assembly-qualified, so they resolve whichever assembly started the process.
    private static readonly Uri ReadyIconUri = new("pack://application:,,,/Murmur.UI;component/Assets/tray-ready.ico", UriKind.Absolute);
    private static readonly Uri BusyIconUri = new("pack://application:,,,/Murmur.UI;component/Assets/tray-busy.ico", UriKind.Absolute);

    private readonly ILogger _logger = Log.ForContext<TrayIconHost>();

    private TaskbarIcon? _icon;
    private bool? _showingReadyIcon;
    private string _hotkeyName = HotkeyDefinition.Default.DisplayName;
    private DateTime _hotkeyReadAt = DateTime.MinValue;
    private bool _disposed;

    public event Action? OpenRequested;

    public event Action? QuitRequested;

    /// <summary>Puts the icon in the notification area. Call once, on the UI thread.</summary>
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

        // Efficiency Mode (the default) would run all of Murmur at idle priority, which delays the keyboard hook and
        // slows transcription.
        _icon.ForceCreate(enablesEfficiencyMode: false);

        engine.StatusChanged += OnStatusChanged;
    }

    /// <summary>The tooltip currently shown, or null before <see cref="Show"/>.</summary>
    internal string? ToolTipText => _icon?.ToolTipText;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        engine.StatusChanged -= OnStatusChanged;
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

    /// <summary>Raised on whichever thread changed the status.</summary>
    private void OnStatusChanged(EngineStatus status) =>
        _icon?.Dispatcher.BeginInvoke(() => Update(rereadHotkey: true));

    private void Update(bool rereadHotkey)
    {
        if (_disposed || _icon is null)
            return;

        var status = engine.Status;
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
            return settings.Load().Hotkey.DisplayName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "Could not read the hotkey for the tray tooltip");
            return _hotkeyName;
        }
    }

    /// <summary>Loads the frame that matches the notification area's icon size at the current DPI.</summary>
    private static DrawingIcon LoadIcon(Uri uri)
    {
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
