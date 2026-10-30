using System.Diagnostics;
using System.Text;
using Murmur.Core.Models;
using Serilog;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Murmur.Core.Speech;

/// <summary>
/// Loads models through Whisper.net: on CUDA when the caller says it can be used (the GPU supports Murmur's CUDA build
/// and the CUDA 13 runtime was found) and the runtime DLLs load (see <see cref="CudaRuntimeLoader"/>), otherwise Vulkan,
/// otherwise the CPU. Warms each model up with a second of zeros, so the one-off cost of the first run is paid while
/// loading rather than on the first dictation.
/// </summary>
public sealed class WhisperModelLoader : IWhisperModelLoader
{
    private const int SampleRate = 16000;

    private static readonly ILogger Logger = Log.ForContext<WhisperModelLoader>();

    /// <summary>
    /// The runtimes Whisper.net tries, in order. CUDA only when it can be used: on a GPU Murmur's CUDA build has kernels
    /// for, with the runtime found. Otherwise a CUDA toolkit on PATH would still put Whisper on a CUDA backend, one that
    /// cannot run on the GPU or that the runtime search was told not to see.
    /// </summary>
    public static RuntimeLibrary[] RuntimeOrder(bool canUseCuda) => canUseCuda
        ? [RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu]
        : [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];

    public async Task<ILoadedSpeechModel> LoadAsync(string modelPath, bool canUseCuda, string? cudaRuntimeDirectory, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var factory = await Task.Run(() => CreateFactory(modelPath, canUseCuda, cudaRuntimeDirectory), ct).ConfigureAwait(false);
        try
        {
            var loaded = stopwatch.Elapsed;
            stopwatch.Restart();
            await LoadedWhisperModel.TranscribeAsync(factory, new float[SampleRate], "", ct).ConfigureAwait(false);
            var runtime = RuntimeOptions.LoadedLibrary?.ToString();
            Logger.Information("Speech model {Path} loaded in {LoadMs:F0} ms and warmed up in {WarmupMs:F0} ms on {Runtime}",
                modelPath, loaded.TotalMilliseconds, stopwatch.Elapsed.TotalMilliseconds, runtime ?? "an unknown runtime");
            return new LoadedWhisperModel(factory, runtime);
        }
        catch
        {
            factory.Dispose();
            throw;
        }
    }

    private static WhisperFactory CreateFactory(string modelPath, bool canUseCuda, string? cudaRuntimeDirectory)
    {
        // Process-wide and only honoured before the first factory loads the native library.
        RuntimeOptions.RuntimeLibraryOrder = [.. RuntimeOrder(canUseCuda)];

        if (!canUseCuda)
            Logger.Information("No GPU that Murmur's CUDA build supports, or no CUDA 13 runtime; the speech engine will use Vulkan or the CPU");
        else if (cudaRuntimeDirectory is not null)
            // Before the factory loads the native library: without it the packaged (Store) build can never use CUDA.
            CudaRuntimeLoader.TryLoad(cudaRuntimeDirectory);

        return WhisperFactory.FromPath(modelPath);
    }

    private sealed class LoadedWhisperModel(WhisperFactory factory, string? runtime) : ILoadedSpeechModel
    {
        public string? Runtime => runtime;

        public Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct) =>
            TranscribeAsync(factory, samples16kMono, prompt, ct);

        public void Dispose() => factory.Dispose();

        internal static async Task<TranscriptionResult> TranscribeAsync(WhisperFactory factory, float[] samples, string prompt, CancellationToken ct)
        {
            var builder = factory.CreateBuilder().WithLanguage("en");
            if (!string.IsNullOrEmpty(prompt))
                builder = builder.WithPrompt(prompt);
            Logger.Debug("Transcribing {Seconds:F1} s of audio with a {PromptLength}-character prompt",
                (double)samples.Length / SampleRate, prompt?.Length ?? 0);

            var text = new StringBuilder();
            var maxNoSpeechProbability = 0f;
            var processor = builder.Build();
            await using var disposal = processor.ConfigureAwait(false);
            await foreach (var segment in processor.ProcessAsync(samples, ct).ConfigureAwait(false))
            {
                var segmentText = segment.Text.Trim();
                if (segmentText.Length > 0)
                {
                    if (text.Length > 0)
                        text.Append(' ');
                    text.Append(segmentText);
                }

                maxNoSpeechProbability = Math.Max(maxNoSpeechProbability, segment.NoSpeechProbability);
            }

            return new TranscriptionResult(text.ToString(), maxNoSpeechProbability);
        }
    }
}
