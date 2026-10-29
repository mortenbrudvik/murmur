using System.Runtime.InteropServices;
using Serilog;

namespace Murmur.UI;

/// <summary>
/// Keeps Murmur to one instance per user session. The first instance owns a named mutex; a later one signals a named
/// event instead, which makes the first instance show its window, and then exits.
/// </summary>
public sealed partial class SingleInstanceGuard : IDisposable
{
    private const string DefaultMutexName = @"Local\Murmur.SingleInstance";
    private const string DefaultShowEventName = @"Local\Murmur.ShowWindow";

    private readonly ILogger _logger = Log.ForContext<SingleInstanceGuard>();
    private readonly string _mutexName;
    private readonly string _showEventName;

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private bool _disposed;

    public SingleInstanceGuard()
        : this(DefaultMutexName, DefaultShowEventName)
    {
    }

    /// <summary>Test seam: lets tests use names of their own instead of the real instance's.</summary>
    internal SingleInstanceGuard(string mutexName, string showEventName)
    {
        _mutexName = mutexName;
        _showEventName = showEventName;
    }

    /// <summary>
    /// Returns true when this is the only instance. Call it on the thread that will later dispose the guard: a mutex
    /// belongs to the thread that acquired it.
    /// </summary>
    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Opened before the mutex, so whenever another instance finds the mutex taken, the event already exists and a
        // signal sent while this instance is still starting up waits for it (the event stays set until it is seen).
        _showEvent ??= OpenShowEvent();

        _mutex ??= new Mutex(initiallyOwned: false, _mutexName);
        try
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance ended without releasing it (a crash): this wait still acquired it.
            _logger.Warning("The previous Murmur instance did not exit cleanly");
            _ownsMutex = true;
        }

        return _ownsMutex;
    }

    /// <summary>Asks the running instance to show its window.</summary>
    public void SignalExisting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // This process was just started by the owner, so it may hand its right to take the foreground over;
        // otherwise Windows would only flash the other instance's taskbar button instead of activating its window.
        AllowSetForegroundWindow(AsfwAny);
        (_showEvent ??= OpenShowEvent()).Set();
        _logger.Information("Murmur is already running; asked it to show its window");
    }

    /// <summary>
    /// Calls <paramref name="onShow"/> whenever another instance signals, including a signal sent before this call.
    /// It runs on the calling thread's <see cref="SynchronizationContext"/> (the UI thread), or on a thread-pool
    /// thread when there is none.
    /// </summary>
    public void ListenForShowRequests(Action onShow)
    {
        ArgumentNullException.ThrowIfNull(onShow);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_showWait is not null)
            throw new InvalidOperationException("Already listening for show requests.");

        var context = SynchronizationContext.Current;
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent ??= OpenShowEvent(),
            (_, _) =>
            {
                if (context is null)
                    InvokeShow(onShow);
                else
                    context.Post(_ => InvokeShow(onShow), null);
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _showWait?.Unregister(null);
        _showEvent?.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex!.ReleaseMutex();
            }
            catch (ApplicationException ex)
            {
                // Disposed on another thread than the one that acquired it; process exit releases it anyway.
                _logger.Debug(ex, "Could not release the single-instance mutex");
            }
        }

        _mutex?.Dispose();
    }

    private EventWaitHandle OpenShowEvent() => new(initialState: false, EventResetMode.AutoReset, _showEventName);

    private void InvokeShow(Action onShow)
    {
        try
        {
            onShow();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not show the window for another instance");
        }
    }

    private const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int dwProcessId);
}
