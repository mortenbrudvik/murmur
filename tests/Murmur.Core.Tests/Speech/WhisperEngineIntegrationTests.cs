using System.Diagnostics;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using Murmur.Core.Hardware;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

/// <summary>
/// Loads the real model on the real GPU, built from the same parts as the app: settings, GPU detection, the CUDA
/// runtime search and Whisper.net. Everything the engine persists goes to a temporary data root, so the app's own
/// settings and models folder (<see cref="MurmurPaths.Default"/>) are never written to. The fresh settings resolve to
/// the model the GPU suits; that model is copied from the app's models folder when it is there, and otherwise
/// downloaded into the temporary root (about 1.6 GB for the large model, 488 MB for the small one). The temporary
/// root is deleted afterwards.
/// <para>
/// The engine starts on the first <see cref="StartAsync"/>, not in an <see cref="IAsyncLifetime.InitializeAsync"/>:
/// xUnit creates and initializes a class fixture even when every test in the class is explicit and left out, so an
/// eager start would copy or download the model in every plain test run.
/// </para>
/// </summary>
public sealed class WhisperEngineFixture : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "murmur-integration-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();
    private readonly Lazy<Task> _start;

    public WhisperEngineFixture() => _start = new(StartEngineAsync);

    public WhisperEngine Engine { get; private set; } = null!;
    public SpeechModelInfo ResolvedModel { get; private set; } = null!;
    public TimeSpan InitializeElapsed { get; private set; }

    /// <summary>Starts the engine once, on the first call; later calls wait for that same start.</summary>
    public Task StartAsync() => _start.Value;

    private async Task StartEngineAsync()
    {
        var paths = new MurmurPaths(_root);
        var settings = new SettingsService(paths, new Win32KeyboardLayout());
        var models = new ModelManager(paths, _http, TimeProvider.System, new DiskSpace());
        var gpu = new GpuInfo(paths);
        var selection = new ModelSelection(settings, gpu, models);

        // Saves the choice in the temporary settings, so the engine's own start resolves the same model.
        ResolvedModel = SpeechModelCatalog.Get(selection.ResolveActive());
        CopyFromAppModels(ResolvedModel, models);

        Engine = new WhisperEngine(models, selection, gpu, new CudaRuntimeLocator(paths), new WhisperModelLoader(), settings,
            TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();
        await Engine.InitializeAsync(CancellationToken.None);
        InitializeElapsed = stopwatch.Elapsed;
    }

    public async ValueTask DisposeAsync()
    {
        if (_start.IsValueCreated)
        {
            // A start that failed has already failed the tests that awaited it; here it only has to be over.
            await _start.Value.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        Engine?.Dispose();
        _http.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Reads (never writes) the app's models folder, to spare a download when the model is already there.</summary>
    private static void CopyFromAppModels(SpeechModelInfo model, ModelManager models)
    {
        var source = new FileInfo(Path.Combine(MurmurPaths.Default.ModelsDirectory, model.FileName));
        if (!source.Exists || source.Length != model.SizeBytes)
            return;

        var target = models.PathFor(model);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        source.CopyTo(target);
    }
}

/// <summary>
/// Explicit, so a plain <c>dotnet test</c> leaves them out: they need the real GPU, a copy or download of a
/// 0.5-1.6 GB model and a minute or more. Run them by hand with
/// <c>dotnet test tests/Murmur.Core.Tests -p:SkipFrontendBuild=true --filter "FullyQualifiedName~WhisperEngineIntegrationTests" -- xUnit.Explicit=on</c>.
/// </summary>
public sealed class WhisperEngineIntegrationTests(WhisperEngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<WhisperEngineFixture>
{
    private const string Sentence = "The quick brown fox jumps over the lazy dog.";

    private async Task<WhisperEngine> ReadyAsync()
    {
        await fixture.StartAsync();
        var status = fixture.Engine.Status;
        status.State.ShouldBe(EngineState.Ready, $"engine failed to start: {status.Error}");
        return fixture.Engine;
    }

    [Fact(Explicit = true)]
    public async Task Initialize_ReachesReady()
    {
        await fixture.StartAsync();
        var status = fixture.Engine.Status;

        output.WriteLine($"Status: {status.State}, model: {status.ModelName}, runtime: {status.Runtime ?? "(unknown)"}, "
            + $"resolved model: {fixture.ResolvedModel.DisplayName}, "
            + $"initialize (verify or download + load + warm-up) took {fixture.InitializeElapsed.TotalMilliseconds:F0} ms. Error: {status.Error ?? "(none)"}");
        status.State.ShouldBe(EngineState.Ready, status.Error);
        status.ModelName.ShouldBe(fixture.ResolvedModel.DisplayName);
        status.Runtime.ShouldNotBeNullOrEmpty();
    }

    [Fact(Explicit = true)]
    public async Task Transcribe_SynthesizedSentence_ContainsWords()
    {
        var engine = await ReadyAsync();
        var samples = Synthesize(Sentence);

        var result = await engine.TranscribeAsync(samples, "", TestContext.Current.CancellationToken);

        output.WriteLine($"Transcript: \"{result.Text}\" (no-speech {result.MaxNoSpeechProbability:F3})");
        result.Text.ToLowerInvariant().ShouldContain("quick brown fox");
    }

    [Fact(Explicit = true)]
    public async Task Transcribe_WithVocabularyPrompt_StillTranscribes()
    {
        var engine = await ReadyAsync();
        var samples = Synthesize(Sentence);

        var result = await engine.TranscribeAsync(samples, "Vocabulary: Murmur, Whisper.", TestContext.Current.CancellationToken);

        output.WriteLine($"Transcript: \"{result.Text}\"");
        result.Text.ToLowerInvariant().ShouldContain("quick brown fox");
    }

    [Fact(Explicit = true)]
    public async Task Transcribe_Silence_IsEmptyOrNoSpeech()
    {
        var engine = await ReadyAsync();

        var result = await engine.TranscribeAsync(new float[3 * 16000], "", TestContext.Current.CancellationToken);

        output.WriteLine($"Silence transcript: \"{result.Text}\" (no-speech {result.MaxNoSpeechProbability:F3})");
        (new TranscriptCleaner().Process(result.Text) == "" || result.MaxNoSpeechProbability >= 0.6f).ShouldBeTrue(
            $"\"{result.Text}\" with no-speech {result.MaxNoSpeechProbability:F3}");
    }

    // Whisper answers "Thank you." for zeros and Whisper.net always reports a no-speech probability of 0, so
    // the engine itself has to recognise a dead input.
    [Fact(Explicit = true)]
    public async Task Transcribe_DigitalSilence_ReturnsNoSpeechWithoutRunningTheModel()
    {
        var engine = await ReadyAsync();
        var stopwatch = Stopwatch.StartNew();

        var result = await engine.TranscribeAsync(new float[3 * 16000], "", TestContext.Current.CancellationToken);

        output.WriteLine($"Digital silence answered in {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        result.Text.ShouldBe("");
        result.MaxNoSpeechProbability.ShouldBe(1f);
    }

    // The speed goal (a 5 s dictation in under 1 s on CUDA) is reported, not asserted: wall-clock time depends on the
    // GPU, the model the PC resolves to and whatever else the machine is doing, so a limit here would only be flaky.
    [Fact(Explicit = true)]
    public async Task Transcribe_FiveSecondSentence_ContainsWordsAndReportsTime()
    {
        var engine = await ReadyAsync();
        var samples = SynthesizeFiveSeconds();

        var stopwatch = Stopwatch.StartNew();
        var result = await engine.TranscribeAsync(samples, "", TestContext.Current.CancellationToken);
        var elapsed = stopwatch.Elapsed;

        output.WriteLine($"{samples.Length / 16000.0:F1} s of audio transcribed in {elapsed.TotalMilliseconds:F0} ms "
            + $"(goal on CUDA: under 1000 ms) with {engine.Status.ModelName} on {engine.Status.Runtime}: \"{result.Text}\"");
        result.Text.ToLowerInvariant().ShouldContain("quick brown fox");
    }

    [Fact(Explicit = true)]
    public async Task Initialize_OnCuda_ReportsCudaInUse()
    {
        var status = (await ReadyAsync()).Status;

        output.WriteLine($"Runtime: {status.Runtime ?? "(unknown)"}, GPU advice: {status.Gpu}");
        Assert.SkipUnless(status.Runtime?.StartsWith("Cuda", StringComparison.Ordinal) == true, "not on CUDA");
        status.Gpu.ShouldNotBeNull().State.ShouldBe(GpuAdviceState.CudaInUse);
        status.Gpu.GpuName.ShouldNotBeNullOrEmpty();
    }

    /// <summary>One spoken sentence in a clip of exactly 5 s, the length of a typical dictation.</summary>
    private static float[] SynthesizeFiveSeconds()
    {
        var spoken = Synthesize(Sentence);
        var clip = new float[5 * 16000];
        spoken.AsSpan(0, Math.Min(spoken.Length, clip.Length)).CopyTo(clip);
        return clip;
    }

    /// <summary>Speaks <paramref name="text"/> with the Windows voice and returns 16 kHz mono floats.</summary>
    private static float[] Synthesize(string text)
    {
        using var pcm = new MemoryStream();
        using (var synthesizer = new SpeechSynthesizer())
        {
            synthesizer.SetOutputToAudioStream(pcm, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synthesizer.Speak(text);
        }

        var bytes = pcm.ToArray();
        // SetOutputToAudioStream writes raw PCM; a RIFF header would mean 44 bytes of non-audio at the start.
        var offset = bytes.Length > 44 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' ? 44 : 0;
        var samples = new float[(bytes.Length - offset) / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, offset + i * 2) / 32768f;
        return samples;
    }
}
