using System.Diagnostics;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.UI;

/// <summary>
/// Carries the web UI's restart and quit requests to whoever owns the process (<see cref="App"/> subscribes and quits),
/// and knows whether Murmur is on its way out, for the windows that close differently then. A restart starts the new
/// Murmur itself, before it says so, so that a new Murmur that cannot be started is an error for the caller.
/// </summary>
public sealed class AppLifecycle : IAppLifecycle
{
    private readonly ILogger _logger = Log.ForContext<AppLifecycle>();
    private readonly Action _startNewInstance;
    private readonly Lock _restartLock = new();
    private volatile bool _isQuitting;
    private bool _restartStarted;

    public AppLifecycle()
        : this(StartNewInstance)
    {
    }

    /// <param name="startNewInstance">Starts the new Murmur; throws when it cannot.</param>
    internal AppLifecycle(Action startNewInstance)
    {
        _startNewInstance = startNewInstance;
    }

    /// <summary>
    /// Raised on whichever thread asked, once the new Murmur has started: this one must now quit, so that the new one
    /// gets the single-instance mutex.
    /// </summary>
    public event Action? RestartRequested;

    /// <summary>Raised on whichever thread asked.</summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Murmur is exiting (Quit, a restart, or Windows signing out). The main window then really closes instead of
    /// hiding in the tray.
    /// </summary>
    public bool IsQuitting => _isQuitting;

    /// <summary>
    /// Starts a new Murmur on Settings, then raises <see cref="RestartRequested"/>. The new instance waits for this one
    /// to let go of the single-instance mutex (<see cref="LaunchArguments.RestartArguments"/>). When the new one cannot
    /// be started the exception is thrown and this one keeps running. Does nothing while Murmur is quitting, or once a
    /// restart has started a new Murmur, so two quick requests do not start two.
    /// </summary>
    public void RequestRestart()
    {
        lock (_restartLock)
        {
            if (_isQuitting || _restartStarted)
            {
                _logger.Information("Restart requested while Murmur is already exiting; ignored");
                return;
            }

            // A failure here is logged by the caller, which reports it: the IPC bridge, or the failure panel's command.
            _logger.Information("Restart requested; starting a new Murmur");
            _startNewInstance();
            _restartStarted = true;
        }

        RestartRequested?.Invoke();
    }

    public void RequestQuit()
    {
        _logger.Information("Quit requested from the window");
        QuitRequested?.Invoke();
    }

    /// <summary>Records that Murmur is exiting; there is no way back.</summary>
    public void MarkQuitting() => _isQuitting = true;

    private static void StartNewInstance()
    {
        // Whether a child started this way keeps the MSIX package identity is checked in Task 11 of the first-run
        // setup plan (packaged install); until then, no fallback.
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, LaunchArguments.RestartArguments) { UseShellExecute = false })
            ?.Dispose();
    }
}
