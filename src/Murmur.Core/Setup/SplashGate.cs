namespace Murmur.Core.Setup;

/// <summary>Why the splash closed.</summary>
public enum SplashCloseReason
{
    /// <summary>The web UI had drawn its first page and the minimum time had passed.</summary>
    Ready,

    /// <summary>The user clicked the splash, pressed a key, or asked for the main window some other way.</summary>
    Skipped,

    /// <summary>The web UI never reported in: the main window is shown anyway.</summary>
    TimedOut
}

/// <summary>
/// Decides when the splash closes: when the UI is ready and the minimum time has passed, at once when the user skips
/// it, and at the timeout whatever happens, so it can never get stuck. The clock starts when the gate is created,
/// which is when the splash is shown. No WPF in here: the window only listens for <see cref="Closed"/>.
/// </summary>
public sealed class SplashGate : IDisposable
{
    /// <summary>Long enough for the name to finish typing (the owner's choice).</summary>
    public static readonly TimeSpan DefaultMinimum = TimeSpan.FromSeconds(2.5);

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    private readonly Lock _lock = new();
    private readonly ITimer _minimumTimer;
    private readonly ITimer _timeoutTimer;
    private bool _ready;
    private bool _minimumPassed;
    private bool _closed;
    private bool _disposed;

    public SplashGate(TimeProvider time, TimeSpan minimum, TimeSpan timeout)
    {
        _minimumTimer = time.CreateTimer(_ => OnMinimumPassed(), null, minimum, Timeout.InfiniteTimeSpan);
        _timeoutTimer = time.CreateTimer(_ => Close(SplashCloseReason.TimedOut), null, timeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised exactly once, on whichever thread caused it: the caller of a method here, or a timer thread.</summary>
    public event Action<SplashCloseReason>? Closed;

    public bool IsClosed
    {
        get
        {
            lock (_lock)
                return _closed;
        }
    }

    /// <summary>The web UI has drawn its first page.</summary>
    public void MarkReady()
    {
        lock (_lock)
        {
            _ready = true;
            if (!_minimumPassed)
                return;
        }

        Close(SplashCloseReason.Ready);
    }

    /// <summary>Close now, drawn or not.</summary>
    public void Skip() => Close(SplashCloseReason.Skipped);

    /// <summary>Stops the clock. A gate disposed before it closed never raises <see cref="Closed"/>.</summary>
    public void Dispose()
    {
        lock (_lock)
            _disposed = true;

        _minimumTimer.Dispose();
        _timeoutTimer.Dispose();
    }

    private void OnMinimumPassed()
    {
        lock (_lock)
        {
            _minimumPassed = true;
            if (!_ready)
                return;
        }

        Close(SplashCloseReason.Ready);
    }

    private void Close(SplashCloseReason reason)
    {
        lock (_lock)
        {
            if (_closed || _disposed)
                return;

            _closed = true;
        }

        Closed?.Invoke(reason);
    }
}
