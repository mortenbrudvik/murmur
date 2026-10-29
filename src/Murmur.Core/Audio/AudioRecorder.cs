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
    // Event-driven capture wakes up every device period (about 10 ms) instead of polling at half the buffer
    // length, so the first chunk arrives sooner and Stop loses at most a few milliseconds of trailing audio.
    private const bool UseEventSync = true;
    private const int BufferMilliseconds = 100;

    private readonly ILogger _logger = Log.ForContext<AudioRecorder>();

    // Guards _session and the sample buffer inside it. The capture thread takes it for every DataAvailable
    // and raises SamplesAvailable while holding it, so once Stop/Cancel has detached the session (which needs
    // the same lock) no further event can be raised for that recording.
    private readonly object _gate = new();
    private Session? _session;

    public event Action<float[]>? SamplesAvailable;

    public void Start(string? deviceId)
    {
        lock (_gate)
        {
            if (_session is not null)
                throw new InvalidOperationException("A recording is already in progress.");

            MMDevice? device = null;
            WasapiCapture? capture = null;
            try
            {
                device = OpenCaptureDevice(deviceId);
                capture = new WasapiCapture(device, UseEventSync, BufferMilliseconds);

                var format = capture.WaveFormat;
                if (!SampleConverter.IsSupported(format))
                    throw new AudioDeviceException(
                        $"The microphone '{device.FriendlyName}' uses an unsupported audio format ({format}).");

                var session = new Session(device, capture, format);
                capture.DataAvailable += (_, e) => OnDataAvailable(session, e);
                capture.RecordingStopped += (_, e) => OnRecordingStopped(session, e);

                // Attached before StartRecording so the very first buffer is not dropped.
                _session = session;
                capture.StartRecording();
                _logger.Debug("Recording started from {Device} ({Format})", device.FriendlyName, format);
            }
            catch (Exception ex)
            {
                _session = null;
                Release(capture, device);

                if (ex is AudioDeviceException)
                    throw;
                throw new AudioDeviceException(
                    deviceId is null
                        ? $"Could not start recording from the default microphone: {ex.Message}"
                        : $"Could not start recording from microphone '{deviceId}': {ex.Message}",
                    ex);
            }
        }
    }

    public float[] Stop()
    {
        var session = Detach();
        if (session is null)
            return [];

        Release(session.Capture, session.Device);

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

        Release(session.Capture, session.Device);
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

    private static MMDevice OpenCaptureDevice(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();

        var device = deviceId is null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
            : enumerator.GetDevice(deviceId);

        if (device.DataFlow != DataFlow.Capture)
        {
            device.Dispose();
            throw new AudioDeviceException($"Audio device '{deviceId}' is not a microphone.");
        }

        if (device.State != DeviceState.Active)
        {
            var state = device.State;
            device.Dispose();
            throw new AudioDeviceException($"Microphone '{deviceId}' is not available ({state}).");
        }

        return device;
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

    private void OnRecordingStopped(Session session, StoppedEventArgs e)
    {
        if (e.Exception is null)
            return;

        bool stillRecording;
        lock (_gate)
            stillRecording = ReferenceEquals(_session, session);

        // Still attached means the device failed (for example it was unplugged) mid-recording; Stop will
        // return whatever was captured until then.
        if (stillRecording)
            _logger.Error(e.Exception, "Audio capture stopped unexpectedly during a recording");
        else
            _logger.Warning(e.Exception, "Audio capture reported an error while shutting down");
    }

    /// <summary>Stops capture and releases the device so Windows' "microphone in use" indicator turns off.</summary>
    private void Release(WasapiCapture? capture, MMDevice? device)
    {
        try
        {
            capture?.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not stop audio capture cleanly");
        }

        try
        {
            capture?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not dispose the audio capture");
        }

        try
        {
            device?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not dispose the audio device");
        }
    }

    private sealed class Session(MMDevice device, WasapiCapture capture, WaveFormat format)
    {
        public MMDevice Device { get; } = device;

        public WasapiCapture Capture { get; } = capture;

        public WaveFormat Format { get; } = format;

        public Resampler16k Resampler { get; } = new(format.SampleRate);

        public List<float> Samples { get; } = [];
    }
}
