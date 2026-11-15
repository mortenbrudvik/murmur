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

    event EventHandler<WaveInEventArgs>? DataAvailable;

    event EventHandler<StoppedEventArgs>? RecordingStopped;

    void StartRecording();
}

/// <summary>A WASAPI shared-mode capture endpoint.</summary>
internal sealed class WasapiCaptureDevice : ICaptureDevice
{
    // Event-driven capture wakes up every device period (about 10 ms) instead of polling at half the buffer
    // length, so the first chunk arrives sooner and Stop loses at most a few milliseconds of trailing audio.
    private const bool UseEventSync = true;
    private const int BufferMilliseconds = 100;

    private readonly ILogger _logger = Log.ForContext<WasapiCaptureDevice>();
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;

    private WasapiCaptureDevice(MMDevice device, WasapiCapture capture, string id, string name)
    {
        _device = device;
        _capture = capture;
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add => _capture.DataAvailable += value;
        remove => _capture.DataAvailable -= value;
    }

    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add => _capture.RecordingStopped += value;
        remove => _capture.RecordingStopped -= value;
    }

    /// <summary>Opens the default capture device (<c>Role.Console</c>) or the one with the given id.</summary>
    public static ICaptureDevice Open(string? deviceId)
    {
        var device = OpenEndpoint(deviceId);
        WasapiCapture? capture = null;
        try
        {
            // Read now, so nothing touches the device's property store once recording has begun.
            var id = device.ID;
            var name = device.FriendlyName;
            capture = new WasapiCapture(device, UseEventSync, BufferMilliseconds);
            return new WasapiCaptureDevice(device, capture, id, name);
        }
        catch
        {
            capture?.Dispose();
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

    public void StartRecording() => _capture.StartRecording();

    public void Dispose()
    {
        // Each step is attempted regardless of the previous one so the device is always released.
        try
        {
            _capture.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not stop audio capture cleanly");
        }

        try
        {
            _capture.Dispose();
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
