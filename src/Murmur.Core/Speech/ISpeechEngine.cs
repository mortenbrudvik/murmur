using Murmur.Core.Models;

namespace Murmur.Core.Speech;

public interface ISpeechEngine
{
    EngineStatus Status { get; }

    /// <summary>Raised on whichever thread changed the status, one change at a time and in order.</summary>
    event Action<EngineStatus>? StatusChanged;

    /// <summary>
    /// Downloads the chosen model if missing, loads it, warms it up and ends in Ready. Failures end in Error; it never
    /// throws. Cancelling <paramref name="ct"/> also cancels any later <see cref="SwitchModelAsync"/>.
    /// </summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>
    /// Transcribes 16 kHz mono samples. Throws <see cref="InvalidOperationException"/> until a model has been loaded;
    /// while a switch prepares the next model, the current one keeps transcribing.
    /// </summary>
    Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct);

    /// <summary>
    /// Makes <paramref name="model"/> the speech model: downloads and loads it while the current one stays Ready, then
    /// swaps them and deletes every other model's files. A failed switch keeps the current model, reports why in
    /// <see cref="EngineStatus.Error"/> and makes it the choice again. Persists the choice before the returned task's
    /// first await; uses the token given to <see cref="InitializeAsync"/>. Completes when the switch has ended; never
    /// throws.
    /// </summary>
    Task SwitchModelAsync(SpeechModelId model);
}
