using Murmur.Core.Services;
using Serilog;

namespace Murmur.UI;

/// <summary>
/// Carries the web UI's restart request to whoever owns the process. Nothing is subscribed yet, so for now a request
/// is only logged.
/// </summary>
public sealed class AppLifecycle : IAppLifecycle
{
    private readonly ILogger _logger = Log.ForContext<AppLifecycle>();

    /// <summary>Raised on whichever thread asked.</summary>
    public event Action? RestartRequested;

    public void RequestRestart()
    {
        _logger.Information("Restart requested");
        RestartRequested?.Invoke();
    }
}
