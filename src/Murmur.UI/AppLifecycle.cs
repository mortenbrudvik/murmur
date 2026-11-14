using Murmur.Core.Services;
using Serilog;

namespace Murmur.UI;

/// <summary>
/// Carries the web UI's restart and quit requests to whoever owns the process (<see cref="App"/> subscribes and does
/// it), and knows whether Murmur is on its way out, for the windows that close differently then.
/// </summary>
public sealed class AppLifecycle : IAppLifecycle
{
    private readonly ILogger _logger = Log.ForContext<AppLifecycle>();
    private volatile bool _isQuitting;

    /// <summary>Raised on whichever thread asked.</summary>
    public event Action? RestartRequested;

    /// <summary>Raised on whichever thread asked.</summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Murmur is exiting (Quit, a restart, or Windows signing out). The main window then really closes instead of
    /// hiding in the tray.
    /// </summary>
    public bool IsQuitting => _isQuitting;

    public void RequestRestart()
    {
        _logger.Information("Restart requested");
        RestartRequested?.Invoke();
    }

    public void RequestQuit()
    {
        _logger.Information("Quit requested from the window");
        QuitRequested?.Invoke();
    }

    /// <summary>Records that Murmur is exiting; there is no way back.</summary>
    public void MarkQuitting() => _isQuitting = true;
}
