using System.Diagnostics;
using System.Text;
using Murmur.Core.Audio;
using Murmur.Core.Models;
using Serilog;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Murmur.Core.Speech;

/// <summary>
/// Whisper large-v3-turbo through Whisper.net, on CUDA when the CUDA 13 runtime DLLs can be found (see
/// <see cref="CudaRuntimeSearch"/> and <see cref="CudaRuntimeLoader"/>), otherwise Vulkan, otherwise the CPU.
/// <see cref="InitializeAsync"/> warms the model up with a second of zeros, so the one-off cost of the first run is
/// paid before it reports Ready rather than on the first dictation.
/// </summary>
public sealed class WhisperEngine(IModelManager models) : ISpeechEngine, IDisposable
{
    public const string ModelName = "Whisper large-v3-turbo";

    private const int SampleRate = 16000;

    /// <summary>Far below any real microphone's noise floor (about -70 dBFS), so only a dead input falls under it.</summary>
    private const float DigitalSilenceDbfs = -80f;

    private static readonly SpeechModelInfo Model = SpeechModelCatalog.LargeV3Turbo;

    private readonly ILogger _logger = Log.ForContext<WhisperEngine>();
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private EngineStatus _status = new(EngineState.NotStarted, ModelName);
    private WhisperFactory? _factory;
    private int _lastReportedPercent;
    private bool _disposed;

    public EngineStatus Status => Volatile.Read(ref _status);

    public event Action<EngineStatus>? StatusChanged;

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            await _initializeGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (_disposed || Status.State == EngineState.Ready)
                return;

            await InitializeCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var factory = Volatile.Read(ref _factory);
        if (Status.State != EngineState.Ready || factory is null)
            throw new InvalidOperationException($"The speech engine is not ready (state: {Status.State}).");

        // Whisper invents text ("Thank you.") for digital silence, and the no-speech probability that
        // Whisper.net reports is always 0, so the model cannot flag it. Answer for it instead.
        if (SpeechDetector.RmsDbfs(samples16kMono) <= DigitalSilenceDbfs)
        {
            _logger.Debug("Skipping transcription of {Seconds:F1} s of digital silence", (double)samples16kMono.Length / SampleRate);
            return new TranscriptionResult("", 1f);
        }

        return await TranscribeCoreAsync(factory, samples16kMono, prompt, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Exchange(ref _factory, null)?.Dispose();
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        var loading = false;
        try
        {
            var modelPath = await EnsureModelAsync(ct).ConfigureAwait(false);

            loading = true;
            SetStatus(new EngineStatus(EngineState.Loading, ModelName));
            var stopwatch = Stopwatch.StartNew();
            var factory = await Task.Run(() => CreateFactory(modelPath), ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _factory, factory)?.Dispose();
            var loaded = stopwatch.Elapsed;

            stopwatch.Restart();
            await TranscribeCoreAsync(factory, new float[SampleRate], "", ct).ConfigureAwait(false);
            var runtime = RuntimeOptions.LoadedLibrary?.ToString();
            _logger.Information("Speech model loaded in {LoadMs:F0} ms and warmed up in {WarmupMs:F0} ms on {Runtime}",
                loaded.TotalMilliseconds, stopwatch.Elapsed.TotalMilliseconds, runtime ?? "an unknown runtime");

            SetStatus(new EngineStatus(EngineState.Ready, ModelName, runtime));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Information("Speech engine start-up was cancelled");
            SetStatus(new EngineStatus(EngineState.NotStarted, ModelName));
        }
        catch (Exception ex)
        {
            var step = loading ? "load the speech model" : "download the speech model";
            _logger.Error(ex, "Speech engine failed to {Step}", step);
            var hint = ex switch
            {
                HttpRequestException => " Check the internet connection and restart Murmur.",
                _ when loading => $" If the model file is damaged, delete {models.PathFor(Model)} and restart Murmur to download it again.",
                _ => ""
            };
            SetStatus(new EngineStatus(EngineState.Error, ModelName, Error: $"Could not {step}: {ex.Message}{hint}"));
        }
    }

    private async Task<string> EnsureModelAsync(CancellationToken ct)
    {
        _lastReportedPercent = 0;
        if (!models.HasExpectedFile(Model))
            SetStatus(new EngineStatus(EngineState.Downloading, ModelName, DownloadProgress: 0));

        // Also verifies a model file that is already there, which reports nothing while it hashes it once.
        return await models.EnsureModelAsync(Model, new InlineProgress(ReportDownloadProgress), ct).ConfigureAwait(false);
    }

    /// <summary>Raises the status only when the whole percent changes: a 1.6 GB download makes thousands of reads.</summary>
    private void ReportDownloadProgress(DownloadProgress progress)
    {
        var fraction = progress.TotalBytes > 0 ? Math.Min((double)progress.BytesReceived / progress.TotalBytes, 1.0) : 0;
        var percent = (int)(fraction * 100);
        if (percent == _lastReportedPercent)
            return;

        _lastReportedPercent = percent;
        SetStatus(new EngineStatus(EngineState.Downloading, ModelName, DownloadProgress: percent / 100.0));
    }

    private static WhisperFactory CreateFactory(string modelPath)
    {
        // Process-wide and only honored before the first factory loads the native library.
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];

        // Before the factory loads the native library: without it the packaged (Store) build can never use CUDA.
        var cudaDirectory = CudaRuntimeLoader.FindRuntimeDirectory(CudaRuntimeSearch.Directories(CudaRuntimeSearch.FromSystem()), File.Exists);
        if (cudaDirectory is not null)
            CudaRuntimeLoader.TryLoad(cudaDirectory);
        else
            Log.ForContext<WhisperEngine>().Information("No CUDA 13 runtime found; the speech engine will use Vulkan or the CPU");
        return WhisperFactory.FromPath(modelPath);
    }

    private async Task<TranscriptionResult> TranscribeCoreAsync(WhisperFactory factory, float[] samples, string prompt, CancellationToken ct)
    {
        var builder = factory.CreateBuilder().WithLanguage("en");
        if (!string.IsNullOrEmpty(prompt))
            builder = builder.WithPrompt(prompt);
        _logger.Debug("Transcribing {Seconds:F1} s of audio with a {PromptLength}-character prompt",
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

    private void SetStatus(EngineStatus status)
    {
        Volatile.Write(ref _status, status);
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "A StatusChanged handler threw while handling {State}", status.State);
        }
    }

    /// <summary>Unlike <see cref="Progress{T}"/> it runs inline, so reports cannot arrive out of order.</summary>
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
