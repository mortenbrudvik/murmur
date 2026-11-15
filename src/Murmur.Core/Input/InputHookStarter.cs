using Serilog;

namespace Murmur.Core.Input;

/// <summary>
/// Starts Murmur's keyboard hook and, if Windows refuses it, tries once more after <see cref="RetryDelay"/> with a
/// fresh <see cref="InputHookHost"/>: a host cannot be started twice, and a refusal at sign-in is sometimes only
/// Windows not being ready yet.
/// </summary>
/// <param name="createHost">Makes a new, unstarted host; called once per attempt.</param>
/// <param name="time">The clock the wait between the attempts runs on.</param>
public sealed class InputHookStarter(Func<InputHookHost> createHost, TimeProvider time)
{
    /// <summary>How long to wait after a failed attempt before the second one.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger = Log.ForContext<InputHookStarter>();

    /// <summary>Why the last attempt failed; null while no attempt has failed since the last success.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>
    /// Starts a host and returns it. The first host is made and its thread started on the caller's thread; the rest runs
    /// on the thread pool, and nothing blocks the caller's thread. Returns null, with <see cref="Failure"/> set, if both
    /// attempts fail. Never throws.
    /// </summary>
    public async Task<InputHookHost?> StartAsync()
    {
        var host = await TryStartAsync().ConfigureAwait(false);
        if (host is not null)
            return host;

        _logger.Warning(Failure, "Could not install the keyboard hook; trying once more in {Delay}", RetryDelay);
        await Task.Delay(RetryDelay, time).ConfigureAwait(false);

        host = await TryStartAsync().ConfigureAwait(false);
        if (host is null)
            _logger.Error(Failure, "Could not install the keyboard hook; dictation is unavailable");
        else
            _logger.Information("The keyboard hook was installed on the second attempt");

        return host;
    }

    private async Task<InputHookHost?> TryStartAsync()
    {
        InputHookHost? host = null;
        try
        {
            host = createHost();
            await host.StartAsync().ConfigureAwait(false);
            Failure = null;
            return host;
        }
        catch (Exception ex)
        {
            Failure = ex;
            Release(host);
            return null;
        }
    }

    /// <summary>
    /// Lets go of a host that failed to start, before anything else is tried. A start that timed out may still install
    /// its hook later; disposing the host ends that thread, so there are never two hook threads, and no key reaches
    /// the dictation twice.
    /// </summary>
    private void Release(InputHookHost? host)
    {
        try
        {
            host?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not stop the input hook host that failed to start");
        }
    }
}
