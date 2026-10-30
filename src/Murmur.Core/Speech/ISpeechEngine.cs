using Murmur.Core.Models;

namespace Murmur.Core.Speech;

public interface ISpeechEngine
{
    EngineStatus Status { get; }

    /// <summary>
    /// True while a model is loaded and the engine is not disposed, which includes the load of a switched-to model: the
    /// previous one keeps transcribing then, though <see cref="Status"/> is not Ready.
    /// </summary>
    bool CanTranscribe { get; }

    /// <summary>Raised on whichever thread changed the status, one change at a time and in order.</summary>
    event Action<EngineStatus>? StatusChanged;

    /// <summary>
    /// Downloads the chosen model if missing, loads it, warms it up and ends in Ready. Failures end in Error; it never
    /// throws. When the chosen model is missing but another model's file is there (Murmur quit during a switch), it is
    /// Ready on that one first and then switches to the chosen one as <see cref="SwitchModelAsync"/> does, completing
    /// when that switch has ended. Cancelling <paramref name="ct"/> also cancels any later <see cref="SwitchModelAsync"/>.
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
    /// first await; when that fails, the task it returns has already failed with the reason and nothing switches.
    /// Otherwise it uses the token given to <see cref="InitializeAsync"/>, completes when the switch has ended and never
    /// fails.
    /// </summary>
    Task SwitchModelAsync(SpeechModelId model);
}
