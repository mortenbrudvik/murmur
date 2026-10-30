using Serilog;

namespace Murmur.UI;

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
    /// Starts a host and returns it. The first attempt runs on the caller's thread, as a direct start would; the second
    /// one on whichever thread the wait ends on. Returns null, with <see cref="Failure"/> set, if both attempts fail.
    /// Never throws.
    /// </summary>
    public async Task<InputHookHost?> StartAsync()
    {
        var host = TryStart();
        if (host is not null)
            return host;

        _logger.Warning(Failure, "Could not install the keyboard hook; trying once more in {Delay}", RetryDelay);
        await Task.Delay(RetryDelay, time).ConfigureAwait(false);

        host = TryStart();
        if (host is null)
            _logger.Error(Failure, "Could not install the keyboard hook; dictation is unavailable");
        else
            _logger.Information("The keyboard hook was installed on the second attempt");

        return host;
    }

    private InputHookHost? TryStart()
    {
        InputHookHost? host = null;
        try
        {
            host = createHost();
            host.Start();
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
