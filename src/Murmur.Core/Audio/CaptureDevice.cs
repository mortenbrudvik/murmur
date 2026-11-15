using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace Murmur.Core.Audio;

/// <summary>
/// An opened microphone, ready to record. Exists so <see cref="AudioRecorder"/> can be tested without
/// audio hardware; production code uses <see cref="WasapiCaptureDevice"/>. Disposing stops capture and
/// releases the device; it waits for the capture thread, so it must not be called while holding a lock that
/// the capture thread's handlers take.
/// </summary>
internal interface ICaptureDevice : IDisposable
{
    /// <summary>The endpoint's id, read when the device is opened. Tells whether an open device is the one asked for.</summary>
    string Id { get; }

    /// <summary>Friendly name, read when the device is opened (never touches the device afterwards).</summary>
    string Name { get; }

    WaveFormat WaveFormat { get; }

    /// <summary>
    /// Raised on the capture thread once per WASAPI packet. The span is only valid during the call, so handlers copy
    /// what they keep. Packets WASAPI flags as silent arrive as zeros.
    /// </summary>
    event CaptureDataAvailableHandler? DataAvailable;

    event EventHandler<StoppedEventArgs>? RecordingStopped;

    void StartRecording();
}

/// <summary>A WASAPI shared-mode capture endpoint.</summary>
internal sealed class WasapiCaptureDevice : ICaptureDevice
{
    // Event-driven capture (WithEventSync) wakes up every device period (about 10 ms) instead of polling at half the
    // buffer length, so the first chunk arrives sooner and Stop loses at most a few milliseconds of trailing audio.
    private const int BufferMilliseconds = 100;

    private readonly ILogger _logger = Log.ForContext<WasapiCaptureDevice>();
    private readonly MMDevice _device;
    private readonly WasapiRecorder _recorder;

    private WasapiCaptureDevice(MMDevice device, WasapiRecorder recorder, string id, string name)
    {
        _device = device;
        _recorder = recorder;
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public WaveFormat WaveFormat => _recorder.WaveFormat;

    public event CaptureDataAvailableHandler? DataAvailable
    {
        add => _recorder.DataAvailable += value;
        remove => _recorder.DataAvailable -= value;
    }

    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add => _recorder.RecordingStopped += value;
        remove => _recorder.RecordingStopped -= value;
    }

    /// <summary>Opens the default capture device (<c>Role.Console</c>) or the one with the given id.</summary>
    public static ICaptureDevice Open(string? deviceId)
    {
        var device = OpenEndpoint(deviceId);
        WasapiRecorder? recorder = null;
        try
        {
            // Read now, so nothing touches the device's property store once recording has begun.
            var id = device.ID;
            var name = device.FriendlyName;
            recorder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithEventSync()
                .WithBufferLength(BufferMilliseconds)
                .Build();
            return new WasapiCaptureDevice(device, recorder, id, name);
        }
        catch
        {
            recorder?.Dispose();
            device.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The id of the Windows default capture device (<c>Role.Console</c>), or null when there is none or it cannot be
    /// read. Never throws: it only decides whether an open microphone can be used again.
    /// </summary>
    public static string? DefaultId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console))
                return null;

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return device.ID;
        }
        catch (Exception ex)
        {
            Log.ForContext<WasapiCaptureDevice>().Debug(ex, "Could not read the default microphone's id");
            return null;
        }
    }

    public void StartRecording() => _recorder.StartRecording();

    public void Dispose()
    {
        // Each step is attempted regardless of the previous one so the device is always released.
        try
        {
            _recorder.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not stop audio capture cleanly");
        }

        try
        {
            // Waits for the capture thread to finish; the recorder does not dispose the MMDevice, so that follows.
            _recorder.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not dispose the audio capture");
        }

        try
        {
            _device.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not dispose the audio device");
        }
    }

    private static MMDevice OpenEndpoint(string? deviceId)
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
}
