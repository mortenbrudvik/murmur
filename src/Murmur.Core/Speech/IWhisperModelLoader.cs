using Murmur.Core.Models;

namespace Murmur.Core.Speech;

/// <summary>A speech model loaded into a Whisper runtime, ready to transcribe. Disposing it frees the model's memory.</summary>
public interface ILoadedSpeechModel : IDisposable
{
    /// <summary>The runtime it runs on ("Cuda", "Vulkan", "Cpu"); null when Whisper.net did not say.</summary>
    string? Runtime { get; }

    /// <summary>Transcribes 16 kHz mono samples. One at a time: the caller must not overlap calls or dispose during one.</summary>
    Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct);
}

/// <summary>Loads a verified model file into Whisper.</summary>
public interface IWhisperModelLoader
{
    /// <summary>
    /// Sets RuntimeOptions.RuntimeLibraryOrder (<see cref="WhisperModelLoader.RuntimeOrder"/>), preloads the CUDA
    /// runtime from <paramref name="cudaRuntimeDirectory"/> when <paramref name="canUseCuda"/>, loads the model and
    /// warms it up with a second of zeros.
    /// </summary>
    /// <param name="canUseCuda">
    /// This PC has a GPU that Murmur's CUDA build supports and the CUDA 13 runtime was found in
    /// <paramref name="cudaRuntimeDirectory"/>. Otherwise CUDA is never tried, not even from a runtime on the process PATH.
    /// </param>
    /// <param name="cudaRuntimeDirectory">Where the CUDA 13 runtime DLLs are; null when they were not found.</param>
    Task<ILoadedSpeechModel> LoadAsync(string modelPath, bool canUseCuda, string? cudaRuntimeDirectory, CancellationToken ct);
}
