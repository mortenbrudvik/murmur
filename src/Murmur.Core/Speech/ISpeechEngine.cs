using Murmur.Core.Models;

namespace Murmur.Core.Speech;

public interface ISpeechEngine
{
    EngineStatus Status { get; }

    event Action<EngineStatus>? StatusChanged;

    /// <summary>Downloads the model if missing, loads it, warms it up and ends in Ready. Failures end in Error; it never throws.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Transcribes 16 kHz mono samples. Throws <see cref="InvalidOperationException"/> until the engine is Ready.</summary>
    Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct);
}
