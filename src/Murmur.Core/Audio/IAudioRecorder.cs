using Murmur.Core.Models;

namespace Murmur.Core.Audio;

/// <summary>Captures microphone audio as 16 kHz mono float samples (the format Whisper expects).</summary>
public interface IAudioRecorder
{
    /// <summary>
    /// Raised with each new chunk of 16 kHz mono samples, on the capture thread. Handlers must be quick and
    /// must not block on another thread (for example a synchronous dispatch to the UI thread) or call
    /// <see cref="Stop"/> or <see cref="Cancel"/> synchronously: <see cref="Stop"/> waits for a handler that
    /// is running.
    /// </summary>
    event Action<float[]>? SamplesAvailable;

    /// <summary>
    /// Raised when the microphone fails in the middle of a recording (for example it was unplugged or the
    /// audio service restarted), with the underlying error. Raised on the capture/notification thread, not
    /// while the recorder holds its internal lock, and at most once per recording. By the time it is raised
    /// the recorder has already dropped the recording and released the device: the samples captured so far are
    /// discarded, a later <see cref="Start"/> works, <see cref="Stop"/> returns an empty array and
    /// <see cref="Cancel"/> is a no-op.
    /// </summary>
    event Action<Exception>? CaptureFailed;

    /// <summary>
    /// Starts capturing from the given device, or from the Windows default capture device
    /// (<c>Role.Console</c>) when <paramref name="deviceId"/> is <see langword="null"/>.
    /// </summary>
    /// <exception cref="AudioDeviceException">The device is missing, unavailable or cannot be opened.</exception>
    /// <exception cref="InvalidOperationException">A recording is already in progress.</exception>
    void Start(string? deviceId);

    /// <summary>
    /// Stops capturing, releases the device and returns every 16 kHz mono sample recorded since
    /// <see cref="Start"/>. No <see cref="SamplesAvailable"/> is raised once this returns. Returns an empty
    /// array when not recording.
    /// </summary>
    float[] Stop();

    /// <summary>Stops capturing, releases the device and discards the samples. A no-op when not recording.</summary>
    void Cancel();

    /// <summary>Lists the active capture endpoints; <see cref="MicrophoneInfo.IsDefault"/> marks the Console default.</summary>
    IReadOnlyList<MicrophoneInfo> ListMicrophones();
}

/// <summary>A capture device could not be found, opened or used.</summary>
public sealed class AudioDeviceException(string message, Exception? inner = null) : Exception(message, inner);
