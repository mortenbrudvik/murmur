using System.Runtime.ExceptionServices;
using Murmur.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace Murmur.Core.Audio;

/// <summary>
/// WASAPI shared-mode microphone capture, converted to 16 kHz mono float. The device is opened by
/// <see cref="Start"/> and released by <see cref="Stop"/>/<see cref="Cancel"/>, so Windows' "microphone in
/// use" indicator is only on while recording.
/// </summary>
public sealed class AudioRecorder : IAudioRecorder
{
    private readonly ILogger _logger = Log.ForContext<AudioRecorder>();
    private readonly Func<string?, ICaptureDevice> _openDevice;

    // Guards _session and the sample buffer inside it. The capture thread takes it for every DataAvailable
    // and raises SamplesAvailable while holding it, so once Stop/Cancel has detached the session (which needs
    // the same lock) no further event can be raised for that recording. Never hold it while disposing a
    // capture device: disposing waits for the capture thread, which may be waiting for this lock.
    private readonly object _gate = new();
    private Session? _session;

    public AudioRecorder()
        : this(WasapiCaptureDevice.Open)
    {
    }

    /// <summary>Test seam: lets tests supply a fake capture device instead of real audio hardware.</summary>
    internal AudioRecorder(Func<string?, ICaptureDevice> openDevice) => _openDevice = openDevice;

    public event Action<float[]>? SamplesAvailable;

    public event Action<Exception>? CaptureFailed;

    public void Start(string? deviceId)
    {
        ICaptureDevice? device = null;
        Session? started = null;
        Exception? failure = null;

        lock (_gate)
        {
            if (_session is not null)
                throw new InvalidOperationException("A recording is already in progress.");

            try
            {
                device = _openDevice(deviceId);
                if (!SampleConverter.IsSupported(device.WaveFormat))
                    throw new AudioDeviceException(
                        $"The microphone '{device.Name}' uses an unsupported audio format ({device.WaveFormat}).");

                var session = new Session(device);
                device.DataAvailable += (_, e) => OnDataAvailable(session, e);
                device.RecordingStopped += (_, e) => OnRecordingStopped(session, e);

                // Attached before StartRecording so the very first buffer is not dropped. Nothing that can
                // fail (logging, COM property reads) happens after StartRecording while the lock is held.
                _session = session;
                device.StartRecording();
                started = session;
            }
            catch (Exception ex)
            {
                _session = null;
                failure = ex;
            }
        }

        if (failure is null)
        {
            _logger.Debug("Recording started from {Device} ({Format})", started!.Device.Name, started.Format);
            return;
        }

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

    public float[] Stop()
    {
        var session = Detach();
        if (session is null)
            return [];

        ReleaseQuietly(session.Device);

        // Safe without the lock: the session is detached, so the capture thread no longer appends to it.
        var samples = session.Samples.ToArray();
        _logger.Debug("Recording stopped, {Count} samples", samples.Length);
        return samples;
    }

    public void Cancel()
    {
        var session = Detach();
        if (session is null)
            return;

        ReleaseQuietly(session.Device);
        _logger.Debug("Recording cancelled");
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

    private Session? Detach()
    {
        lock (_gate)
        {
            var session = _session;
            _session = null;
            return session;
        }
    }

    /// <summary>Detaches <paramref name="session"/> if it is still the current one (Stop/Cancel may have won).</summary>
    private bool TryDetach(Session session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
                return false;

            _session = null;
            return true;
        }
    }

    /// <summary>Runs on the capture thread.</summary>
    private void OnDataAvailable(Session session, WaveInEventArgs e)
    {
        lock (_gate)
        {
            // Stopped, cancelled or replaced by a newer recording while this buffer was in flight.
            if (!ReferenceEquals(_session, session))
                return;

            float[] chunk;
            try
            {
                var interleaved = SampleConverter.ToFloat(e.Buffer.AsSpan(0, e.BytesRecorded), session.Format);
                var mono = SampleConverter.ToMono(interleaved, session.Format.Channels);
                chunk = session.Resampler.Process(mono);
            }
            catch (Exception ex)
            {
                // An exception on the capture thread would take the whole process down.
                _logger.Error(ex, "Could not convert captured audio");
                return;
            }

            if (chunk.Length == 0)
                return;

            session.Samples.AddRange(chunk);

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

        if (!TryDetach(session))
        {
            // Stop/Cancel got there first (or the recording never got going); nobody is waiting for this.
            _logger.Warning(e.Exception, "Audio capture reported an error while shutting down");
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

    private sealed class Session(ICaptureDevice device)
    {
        public ICaptureDevice Device { get; } = device;

        public WaveFormat Format { get; } = device.WaveFormat;

        public Resampler16k Resampler { get; } = new(device.WaveFormat.SampleRate);

        public List<float> Samples { get; } = [];
    }
}
