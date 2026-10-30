using MediatR;
using Murmur.Common;
using Murmur.Core.Features.Setup;
using Murmur.Core.Models;
using Serilog;

namespace Murmur.Core.Audio;

/// <summary>
/// The microphone test of the first-run setup: opens the microphone on a recorder of its own (so it can never
/// collide with a dictation) and publishes the level of what it hears as <see cref="MicLevelNotification"/>s, at most
/// one per 50 ms, each the loudest level heard since the one before (so a short peak is not lost to the throttle). It
/// never leaves the microphone open: the test ends on <see cref="Stop"/>, on <see cref="StopForWindow"/>,
/// on <see cref="Dispose"/>, when the microphone fails and by itself after <see cref="MaxDuration"/>. A test that ends
/// without the page asking (all but <see cref="Stop"/> and <see cref="Dispose"/>) publishes a last, ended level of 0, so
/// the page knows to start it again.
/// </summary>
/// <remarks>
/// The recorder raises its events on the capture thread, where <see cref="IAudioRecorder.Stop"/> and
/// <see cref="IAudioRecorder.Cancel"/> wait for a running handler. So the sample handler only computes and publishes
/// without waiting (the notification is sent fire-and-forget), and the auto-stop runs on a timer thread. The lock
/// only guards which test is current; no recorder method is ever called while holding it.
/// </remarks>
public sealed class MicrophoneLevelMonitor(Func<IAudioRecorder> createRecorder, IMediator mediator, TimeProvider time) : IDisposable
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);

    /// <summary>The level (dBFS) shown as silence; 0 dBFS is the full-scale level.</summary>
    private const double SilenceDbfs = -60;

    private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(50);

    private readonly ILogger _logger = Log.ForContext<MicrophoneLevelMonitor>();
    private readonly object _gate = new();
    private Session? _current;
    private bool _disposed;

    /// <summary>How long a test may run before it stops by itself, so a forgotten test cannot hold the microphone.</summary>
    public static TimeSpan MaxDuration { get; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Opens the microphone (<paramref name="deviceId"/>, or the Windows default when null) and starts publishing
    /// levels. A test that is already running is stopped first. Never throws: a microphone that cannot be opened is
    /// reported as <see cref="MicTestState.AccessDenied"/> (Windows blocks it) or <see cref="MicTestState.Failed"/>.
    /// </summary>
    public MicTestResult Start(string? deviceId)
    {
        Session session;
        try
        {
            session = new Session(this, createRecorder());
        }
        catch (Exception ex)
        {
            return Failure(ex);
        }

        Session? previous;
        lock (_gate)
        {
            if (_disposed)
                return new MicTestResult(MicTestState.Failed, "The microphone test is no longer available.");

            previous = _current;
            _current = session;
        }

        if (previous is not null)
            End(previous);

        // Listening before the device opens, so that the very first chunk is not missed.
        session.Attach();
        try
        {
            session.Recorder.Start(deviceId);
        }
        catch (Exception ex)
        {
            TryDetach(session);
            End(session);
            return Failure(ex);
        }

        bool stillCurrent;
        lock (_gate)
        {
            stillCurrent = ReferenceEquals(_current, session);
            if (stillCurrent)
                session.Timer = time.CreateTimer(_ => OnAutoStop(session), null, MaxDuration, Timeout.InfiniteTimeSpan);
        }

        if (!stillCurrent)
        {
            // Stop, Dispose or another Start came in while the device was opening. That one has already ended this
            // test, before there was anything to release, so the microphone that has just opened is released here.
            ReleaseQuietly(session.Recorder);
            return new MicTestResult(MicTestState.Failed, "The microphone test was stopped before the microphone opened.");
        }

        _logger.Debug("Microphone test started");
        return new MicTestResult(MicTestState.Listening);
    }

    /// <summary>
    /// Ends the test and releases the microphone, for the page that asked for it: publishes nothing. Harmless when no
    /// test is running.
    /// </summary>
    public void Stop() => StopCurrent(announce: false);

    /// <summary>
    /// Ends the test and releases the microphone because the window was hidden or minimized, and publishes an ended
    /// level of 0, so the page starts the test again once it is seen. Harmless, and silent, when no test is running.
    /// </summary>
    public void StopForWindow() => StopCurrent(announce: true);

    /// <summary>Stops a running test; a test started afterwards is refused.</summary>
    public void Dispose()
    {
        Session? session;
        lock (_gate)
        {
            _disposed = true;
            session = _current;
            _current = null;
        }

        if (session is not null)
            End(session);
    }

    /// <summary>Maps an RMS level in dBFS to the meter: -60 dBFS and below is 0, 0 dBFS and above is 1, linear in between.</summary>
    public static double LevelFromDbfs(double dbfs) => Math.Clamp((dbfs - SilenceDbfs) / -SilenceDbfs, 0, 1);

    /// <summary>
    /// <see cref="MicTestState.AccessDenied"/> when the error, or anything it wraps, is an
    /// <see cref="UnauthorizedAccessException"/> or carries <c>E_ACCESSDENIED</c> (how Windows reports a microphone
    /// that privacy settings block); <see cref="MicTestState.Failed"/> otherwise.
    /// </summary>
    public static MicTestState Classify(Exception ex) =>
        IsAccessDenied(ex) ? MicTestState.AccessDenied : MicTestState.Failed;

    private static bool IsAccessDenied(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is UnauthorizedAccessException || ex.HResult == AccessDeniedHResult)
                return true;

            if (ex is AggregateException aggregate && aggregate.InnerExceptions.Any(IsAccessDenied))
                return true;
        }

        return false;
    }

    private MicTestResult Failure(Exception ex)
    {
        var state = Classify(ex);
        _logger.Warning(ex, "The microphone test could not start ({State})", state);

        // Windows blocking the microphone has its own explanation on the page; the message is for everything else.
        return state == MicTestState.AccessDenied ? new MicTestResult(state) : new MicTestResult(state, ex.Message);
    }

    /// <summary>Runs on the capture thread: must stay quick and never wait for another thread.</summary>
    private void OnSamples(Session session, float[] chunk)
    {
        if (session.Ended)
            return;

        // Only this thread touches the session's publish bookkeeping. A chunk that is not published still counts: the
        // meter shows the loudest level since the last publish, so a short peak is not lost to the 50 ms throttle.
        session.Peak = Math.Max(session.Peak, LevelFromDbfs(SpeechDetector.RmsDbfs(chunk)));

        var now = time.GetTimestamp();
        if (session.HasPublished && time.GetElapsedTime(session.LastPublished, now) < PublishInterval)
            return;

        session.HasPublished = true;
        session.LastPublished = now;
        var level = session.Peak;
        session.Peak = 0;
        Publish(level, ended: false);
    }

    /// <summary>
    /// Runs on the capture thread, after the recorder has already dropped the recording and released the device.
    /// Ends the test so that its timer goes, and zeroes the meter, which would otherwise stay at its last level.
    /// </summary>
    private void OnCaptureFailed(Session session, Exception ex)
    {
        if (!TryDetach(session))
            return;

        _logger.Warning(ex, "The microphone failed during the microphone test");
        End(session);
        PublishEnded();
    }

    /// <summary>Runs on a timer thread, never on the capture thread, so it may wait for the recorder to stop.</summary>
    private void OnAutoStop(Session session)
    {
        if (!TryDetach(session))
            return;

        _logger.Information("The microphone test reached its {Limit} limit and stopped", MaxDuration);
        End(session);
        PublishEnded();
    }

    /// <summary>Ends the current test, if any; <paramref name="announce"/> publishes that it ended.</summary>
    private void StopCurrent(bool announce)
    {
        Session? session;
        lock (_gate)
        {
            session = _current;
            _current = null;
        }

        if (session is null)
            return;

        End(session);
        if (announce)
            PublishEnded();
    }

    /// <summary>Takes <paramref name="session"/> out as the current test; false when something else already did.</summary>
    private bool TryDetach(Session session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_current, session))
                return false;

            _current = null;
            return true;
        }
    }

    /// <summary>Stops listening to the session's recorder, cancels its timer and releases the microphone. Once per session.</summary>
    private void End(Session session)
    {
        ITimer? timer;
        lock (_gate)
        {
            if (!session.TryEnd())
                return;

            timer = session.Timer;
            session.Timer = null;
        }

        timer?.Dispose();
        session.Detach();
        ReleaseQuietly(session.Recorder);
    }

    private void ReleaseQuietly(IAudioRecorder recorder)
    {
        try
        {
            recorder.Cancel();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not release the microphone after the test");
        }
    }

    /// <summary>
    /// A silent meter that tells the page the test is over. Called after <see cref="End"/>: releasing the recorder waits
    /// for a sample handler that is running, so this is the ended test's last publish.
    /// </summary>
    private void PublishEnded() => Publish(0, ended: true);

    /// <summary>Publishes without waiting: the capture thread must not block on the page.</summary>
    private void Publish(double level, bool ended)
    {
        try
        {
            mediator.Publish(new MicLevelNotification(level, ended)).FireAndForget();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not publish the microphone level");
        }
    }

    /// <summary>One run of the test: its recorder, its auto-stop timer and the bookkeeping of the 50 ms throttle and the peak.</summary>
    private sealed class Session
    {
        private readonly MicrophoneLevelMonitor _owner;
        private readonly Action<float[]> _onSamples;
        private readonly Action<Exception> _onCaptureFailed;
        private volatile bool _ended;

        public Session(MicrophoneLevelMonitor owner, IAudioRecorder recorder)
        {
            _owner = owner;
            Recorder = recorder;
            _onSamples = chunk => _owner.OnSamples(this, chunk);
            _onCaptureFailed = ex => _owner.OnCaptureFailed(this, ex);
        }

        public IAudioRecorder Recorder { get; }

        /// <summary>Read by the capture thread without a lock; set once, under the monitor's lock.</summary>
        public bool Ended => _ended;

        /// <summary>The auto-stop timer; guarded by the monitor's lock.</summary>
        public ITimer? Timer { get; set; }

        // Only the capture thread reads and writes these.
        public bool HasPublished { get; set; }

        public long LastPublished { get; set; }

        /// <summary>The loudest level (0 to 1) heard since the last publish; 0 for a new test, which has heard nothing.</summary>
        public double Peak { get; set; }

        /// <summary>Marks the session as ended; false when it already was. Call with the monitor's lock held.</summary>
        public bool TryEnd()
        {
            if (_ended)
                return false;

            _ended = true;
            return true;
        }

        public void Attach()
        {
            Recorder.SamplesAvailable += _onSamples;
            Recorder.CaptureFailed += _onCaptureFailed;
        }

        public void Detach()
        {
            Recorder.SamplesAvailable -= _onSamples;
            Recorder.CaptureFailed -= _onCaptureFailed;
        }
    }
}
