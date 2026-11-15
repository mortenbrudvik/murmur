using System.Runtime.ExceptionServices;
using Murmur.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace Murmur.Core.Audio;

/// <summary>
/// WASAPI shared-mode microphone capture, converted to 16 kHz mono float. By default the device is opened by
/// <see cref="Start"/> and released by <see cref="Stop"/>/<see cref="Cancel"/>, so Windows' "microphone in
/// use" indicator is only on while recording.
/// <para>
/// With <see cref="KeepOpen"/> on, the device stays open (and the indicator on) between recordings, so a recording
/// starts without the device's start-up delay. Nothing is kept of what the open microphone hears in between: its
/// buffers are dropped unconverted, and no event is raised for them.
/// </para>
/// </summary>
public sealed class AudioRecorder : IAudioRecorder, IDisposable
{
    /// <summary>How long <see cref="Dispose"/> waits for the lock before it leaves an open in flight to its owner.</summary>
    private static readonly TimeSpan DisposeLockWait = TimeSpan.FromMilliseconds(200);

    private readonly ILogger _logger = Log.ForContext<AudioRecorder>();
    private readonly Func<string?, ICaptureDevice> _openDevice;
    private readonly Func<string?> _defaultDeviceId;

    // Guards _session, the recording inside it and its sample buffer. The capture thread takes it for every
    // DataAvailable and raises SamplesAvailable while holding it, so once Stop/Cancel has ended the recording (which
    // needs the same lock) no further event can be raised for it. Never hold it while disposing a capture device:
    // disposing waits for the capture thread, which may be waiting for this lock.
    private readonly object _gate = new();

    // The open device. Its Recording is null while the device is only kept open (KeepOpen), between recordings.
    private Session? _session;

    // Written under _gate (Dispose aside), read without it where a stale value is harmless.
    private volatile bool _keepOpen;

    public AudioRecorder()
        : this(WasapiCaptureDevice.Open, WasapiCaptureDevice.DefaultId)
    {
    }

    /// <summary>Test seam: lets tests supply a fake capture device instead of real audio hardware.</summary>
    /// <param name="defaultDeviceId">The id of the Windows default microphone; none when left out.</param>
    internal AudioRecorder(Func<string?, ICaptureDevice> openDevice, Func<string?>? defaultDeviceId = null)
    {
        _openDevice = openDevice;
        _defaultDeviceId = defaultDeviceId ?? (static () => null);
    }

    public event Action<float[]>? SamplesAvailable;

    public event Action<Exception>? CaptureFailed;

    public bool KeepOpen
    {
        get => _keepOpen;
        set
        {
            Session? idle = null;
            lock (_gate)
            {
                _keepOpen = value;
                if (!value)
                    idle = DetachIfIdle();
            }

            if (idle is not null)
            {
                ReleaseQuietly(idle.Device);
                _logger.Information("The microphone is no longer kept open");
            }
        }
    }

    public void Prepare(string? deviceId)
    {
        if (!_keepOpen)
            return;

        var wantedId = deviceId ?? _defaultDeviceId();
        Session? other = null;
        lock (_gate)
        {
            // A recording keeps the device it has; it stays open afterwards.
            if (!_keepOpen || _session is { Recording: not null })
                return;

            if (_session is { } open)
            {
                if (open.Device.Id == wantedId)
                    return;

                other = open;
                _session = null;
            }
        }

        ReleaseQuietly(other?.Device);

        ICaptureDevice? device;
        Session? opened;
        Exception? failure;
        lock (_gate)
        {
            // Turned off, or a recording opened the device, while the other one was being released.
            if (!_keepOpen || _session is not null)
                return;

            opened = Open(deviceId, record: false, out device, out failure);
        }

        if (failure is not null)
            ThrowOpenFailure(deviceId, device, failure);

        _logger.Information("Keeping {Device} open between dictations ({Format})", opened!.Device.Name, opened.Format);
    }

    public void Start(string? deviceId)
    {
        // Only a device kept open can be there already, and only then is the default device's id worth reading.
        var wantedId = _keepOpen ? deviceId ?? _defaultDeviceId() : null;
        Session? kept = null;
        Session? other = null;
        lock (_gate)
        {
            if (_session is { Recording: not null })
                throw new InvalidOperationException("A recording is already in progress.");

            if (_session is { } open)
            {
                if (wantedId is not null && open.Device.Id == wantedId)
                {
                    // Nothing that can fail comes after this while the lock is held.
                    open.Recording = new Recording(open.Format.SampleRate);
                    kept = open;
                }
                else
                {
                    // Another microphone was chosen, or Windows has a new default: this one is no longer wanted.
                    other = open;
                    _session = null;
                }
            }
        }

        if (kept is not null)
        {
            _logger.Debug("Recording started from {Device}, which was kept open ({Format})", kept.Device.Name, kept.Format);
            return;
        }

        ReleaseQuietly(other?.Device);

        ICaptureDevice? device;
        Session? started;
        Exception? failure;
        lock (_gate)
        {
            if (_session is not null)
                throw new InvalidOperationException("A recording is already in progress.");

            started = Open(deviceId, record: true, out device, out failure);
        }

        if (failure is not null)
            ThrowOpenFailure(deviceId, device, failure);

        _logger.Debug("Recording started from {Device} ({Format})", started!.Device.Name, started.Format);
    }

    public float[] Stop()
    {
        var recording = EndRecording(out var released);
        if (recording is null)
            return [];

        ReleaseQuietly(released?.Device);

        // Safe without the lock: the recording is ended, so the capture thread no longer appends to it.
        var samples = recording.Samples.ToArray();
        _logger.Debug("Recording stopped, {Count} samples", samples.Length);
        return samples;
    }

    public void Cancel()
    {
        if (EndRecording(out var released) is null)
            return;

        ReleaseQuietly(released?.Device);
        _logger.Debug("Recording cancelled");
    }

    /// <summary>
    /// Stops keeping the microphone open and releases it. A recording in progress keeps the device until its
    /// <see cref="Stop"/> or <see cref="Cancel"/>, which then releases it. Never waits for a device that is still
    /// being opened (it can take seconds, and this runs on the UI thread when the app exits): that open's owner
    /// releases it, as it would without <see cref="KeepOpen"/>.
    /// </summary>
    public void Dispose()
    {
        _keepOpen = false;
        if (!Monitor.TryEnter(_gate, DisposeLockWait))
            return;

        Session? idle;
        try
        {
            idle = DetachIfIdle();
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        ReleaseQuietly(idle?.Device);
    }

    public IReadOnlyList<MicrophoneInfo> ListMicrophones()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            string? defaultId = null;
            if (enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console))
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                defaultId = defaultDevice.ID;
            }

            var microphones = new List<MicrophoneInfo>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        microphones.Add(new MicrophoneInfo(device.ID, device.FriendlyName, device.ID == defaultId));
                    }
                    catch (Exception ex)
                    {
                        // One misbehaving endpoint should not hide the others.
                        _logger.Warning(ex, "Skipping a capture endpoint whose properties could not be read");
                    }
                }
            }

            return microphones;
        }
        catch (Exception ex)
        {
            // A settings dropdown should degrade to "no microphones" rather than throw.
            _logger.Warning(ex, "Could not enumerate microphones");
            return [];
        }
    }

    /// <summary>
    /// With <see cref="_gate"/> held and no device open: opens <paramref name="deviceId"/> and starts it, recording
    /// from its first buffer when <paramref name="record"/>, otherwise only kept open. On failure nothing is left
    /// open here: <paramref name="failure"/> says why and <paramref name="device"/> is what the caller must release,
    /// outside the lock.
    /// </summary>
    private Session? Open(string? deviceId, bool record, out ICaptureDevice? device, out Exception? failure)
    {
        device = null;
        failure = null;
        try
        {
            device = _openDevice(deviceId);
            if (!SampleConverter.IsSupported(device.WaveFormat))
                throw new AudioDeviceException(
                    $"The microphone '{device.Name}' uses an unsupported audio format ({device.WaveFormat}).");

            var session = new Session(device);
            if (record)
                session.Recording = new Recording(session.Format.SampleRate);

            device.DataAvailable += (buffer, _, _, _) => OnDataAvailable(session, buffer);
            device.RecordingStopped += (_, e) => OnRecordingStopped(session, e);

            // Attached before StartRecording so the very first buffer is not dropped. Nothing that can
            // fail (logging, COM property reads) happens after StartRecording while the lock is held.
            _session = session;
            device.StartRecording();
            return session;
        }
        catch (Exception ex)
        {
            _session = null;
            failure = ex;
            return null;
        }
    }

    /// <summary>Releases what a failed <see cref="Open"/> left and throws its failure as an <see cref="AudioDeviceException"/>.</summary>
    private void ThrowOpenFailure(string? deviceId, ICaptureDevice? device, Exception failure)
    {
        // Outside the lock: if StartRecording got as far as launching the capture thread, that thread may be
        // waiting for _gate in OnDataAvailable, and disposing the device waits for it to finish.
        ReleaseQuietly(device);

        if (failure is AudioDeviceException)
            ExceptionDispatchInfo.Capture(failure).Throw();

        throw new AudioDeviceException(
            deviceId is null
                ? $"Could not start recording from the default microphone: {failure.Message}"
                : $"Could not start recording from microphone '{deviceId}': {failure.Message}",
            failure);
    }

    /// <summary>
    /// Ends the recording in progress and returns it, or null when there is none. The device stays open when
    /// <see cref="KeepOpen"/> is on; otherwise it comes back as <paramref name="released"/>, for the caller to release
    /// outside the lock.
    /// </summary>
    private Recording? EndRecording(out Session? released)
    {
        released = null;
        lock (_gate)
        {
            if (_session is not { Recording: { } recording } session)
                return null;

            session.Recording = null;
            if (!_keepOpen)
            {
                _session = null;
                released = session;
            }

            return recording;
        }
    }

    /// <summary>With <see cref="_gate"/> held: takes out a device that is open with no recording, for the caller to release.</summary>
    private Session? DetachIfIdle()
    {
        if (_session is not { Recording: null } idle)
            return null;

        _session = null;
        return idle;
    }

    /// <summary>
    /// Takes out <paramref name="session"/> if it is still the open device (Stop/Cancel may have won) and says whether
    /// it was recording.
    /// </summary>
    private bool TryDetach(Session session, out bool wasRecording)
    {
        lock (_gate)
        {
            wasRecording = false;
            if (!ReferenceEquals(_session, session))
                return false;

            wasRecording = session.Recording is not null;
            session.Recording = null;
            _session = null;
            return true;
        }
    }

    /// <summary>Runs on the capture thread. <paramref name="buffer"/> is only valid during this call.</summary>
    private void OnDataAvailable(Session session, ReadOnlySpan<byte> buffer)
    {
        lock (_gate)
        {
            // Stopped, cancelled or replaced by a newer recording while this buffer was in flight.
            if (!ReferenceEquals(_session, session))
                return;

            // Only kept open: nobody is recording, so what the microphone hears goes nowhere.
            if (session.Recording is not { } recording)
                return;

            float[] chunk;
            try
            {
                var interleaved = SampleConverter.ToFloat(buffer, session.Format);
                var mono = SampleConverter.ToMono(interleaved, session.Format.Channels);
                chunk = recording.Resampler.Process(mono);
            }
            catch (Exception ex)
            {
                // An exception on the capture thread would take the whole process down.
                _logger.Error(ex, "Could not convert captured audio");
                return;
            }

            if (chunk.Length == 0)
                return;

            recording.Samples.AddRange(chunk);

            try
            {
                SamplesAvailable?.Invoke(chunk);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "A SamplesAvailable handler threw");
            }
        }
    }

    /// <summary>Runs on the capture thread, or on the synchronization context that created the capture.</summary>
    private void OnRecordingStopped(Session session, StoppedEventArgs e)
    {
        // A normal stop (our own Stop/Cancel) carries no exception.
        if (e.Exception is null)
            return;

        if (!TryDetach(session, out var wasRecording))
        {
            // Stop/Cancel got there first (or the recording never got going); nobody is waiting for this.
            _logger.Warning(e.Exception, "Audio capture reported an error while shutting down");
            return;
        }

        if (!wasRecording)
        {
            // Only kept open (the microphone was unplugged, say): nobody loses a recording, and the next Start
            // opens whatever microphone is there then.
            _logger.Warning(e.Exception, "The microphone kept open between dictations failed; it will be opened again when next needed");
            ReleaseQuietly(session.Device);
            return;
        }

        // The device failed mid-recording (for example it was unplugged). Drop the recording and release the
        // device outside the lock so a later Start works, then tell the owner.
        _logger.Error(e.Exception, "Audio capture failed during a recording");
        ReleaseQuietly(session.Device);

        try
        {
            CaptureFailed?.Invoke(e.Exception);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "A CaptureFailed handler threw");
        }
    }

    /// <summary>Stops capture and releases the device so Windows' "microphone in use" indicator turns off.</summary>
    private void ReleaseQuietly(ICaptureDevice? device)
    {
        try
        {
            device?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not release the audio device");
        }
    }

    /// <summary>An open device, with the recording that is being made from it, if any.</summary>
    private sealed class Session(ICaptureDevice device)
    {
        public ICaptureDevice Device { get; } = device;

        public WaveFormat Format { get; } = device.WaveFormat;

        /// <summary>Null while the device is only kept open. Guarded by the recorder's lock.</summary>
        public Recording? Recording { get; set; }
    }

    /// <summary>One recording: its samples so far, and a resampler of its own so that it starts from a clean filter.</summary>
    private sealed class Recording(int sourceRate)
    {
        public Resampler16k Resampler { get; } = new(sourceRate);

        public List<float> Samples { get; } = [];
    }
}
