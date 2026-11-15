using Murmur.Core.Models;
using Serilog;

namespace Murmur.Core.Audio;

/// <summary>
/// Puts the "keep the microphone ready" setting to work on the dictation recorder: on, the recorder keeps its
/// microphone open and the chosen one is opened straight away, so that even the first dictation starts without the
/// microphone's start-up delay; off, a microphone that is only being kept open is released.
/// <para>
/// <see cref="Apply"/> returns at once. Opening a microphone can take seconds, so the work runs on the thread pool,
/// one application after another in the order they were asked for: turning the setting on and off again while the
/// microphone is still opening ends with it released. A microphone that cannot be opened is only logged: the next
/// dictation tries again and reports it, and once open it stays open.
/// </para>
/// </summary>
public sealed class MicrophoneReadiness(IAudioRecorder recorder)
{
    private readonly ILogger _logger = Log.ForContext<MicrophoneReadiness>();
    private readonly Lock _gate = new();

    /// <summary>What <see cref="Apply"/> last started, or a completed task. It never faults. Tests wait for it.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>Applies <paramref name="settings"/> in the background, after anything applied before.</summary>
    public void Apply(MurmurSettings settings)
    {
        var keepReady = settings.KeepMicrophoneReady;
        var microphoneId = settings.MicrophoneId;
        lock (_gate)
        {
            Pending = Pending.ContinueWith(
                _ => ApplyNow(keepReady, microphoneId),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private void ApplyNow(bool keepReady, string? microphoneId)
    {
        try
        {
            recorder.KeepOpen = keepReady;
            if (keepReady)
                recorder.Prepare(microphoneId);
        }
        catch (AudioDeviceException ex)
        {
            // The expected failure: no microphone, or Windows blocks it.
            _logger.Warning(ex, "Could not open the microphone to keep it ready; the next dictation will try again");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not apply the keep-the-microphone-ready setting");
        }
    }
}
