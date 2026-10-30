using System.Diagnostics;
using System.Net;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

/// <summary>
/// Loads the real model on the real GPU. The first run downloads about 1.6 GB into the app's own models
/// folder (<see cref="MurmurPaths.Default"/>), so later runs and the app itself reuse it.
/// </summary>
public sealed class WhisperEngineFixture : IAsyncLifetime
{
    private readonly HttpClient _http = new();

    public WhisperEngine Engine { get; private set; } = null!;
    public TimeSpan InitializeElapsed { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Engine = new WhisperEngine(new ModelManager(MurmurPaths.Default, _http, TimeProvider.System, new DiskSpace()));
        var stopwatch = Stopwatch.StartNew();
        await Engine.InitializeAsync(CancellationToken.None);
        InitializeElapsed = stopwatch.Elapsed;
    }

    public ValueTask DisposeAsync()
    {
        Engine.Dispose();
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class WhisperEngineIntegrationTests(WhisperEngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<WhisperEngineFixture>
{
    private const string Sentence = "The quick brown fox jumps over the lazy dog.";

    private WhisperEngine Ready()
    {
        var status = fixture.Engine.Status;
        status.State.ShouldBe(EngineState.Ready, $"engine failed to start: {status.Error}");
        return fixture.Engine;
    }

    [Fact]
    public void Initialize_ReachesReady()
    {
        var status = fixture.Engine.Status;

        output.WriteLine($"Status: {status.State}, model: {status.ModelName}, runtime: {status.Runtime ?? "(unknown)"}, "
            + $"initialize (download if missing + load + warm-up) took {fixture.InitializeElapsed.TotalMilliseconds:F0} ms. Error: {status.Error ?? "(none)"}");
        status.State.ShouldBe(EngineState.Ready, status.Error);
        status.ModelName.ShouldBe("Whisper large-v3-turbo");
        status.Runtime.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Transcribe_SynthesizedSentence_ContainsWords()
    {
        var engine = Ready();
        var samples = Synthesize(Sentence);

        var result = await engine.TranscribeAsync(samples, "", TestContext.Current.CancellationToken);

        output.WriteLine($"Transcript: \"{result.Text}\" (no-speech {result.MaxNoSpeechProbability:F3})");
        result.Text.ToLowerInvariant().ShouldContain("quick brown fox");
    }

    [Fact]
    public async Task Transcribe_WithVocabularyPrompt_StillTranscribes()
    {
        var engine = Ready();
        var samples = Synthesize(Sentence);

        var result = await engine.TranscribeAsync(samples, "Vocabulary: Murmur, Whisper.", TestContext.Current.CancellationToken);

        output.WriteLine($"Transcript: \"{result.Text}\"");
        result.Text.ToLowerInvariant().ShouldContain("quick brown fox");
    }

    [Fact]
    public async Task Transcribe_Silence_IsEmptyOrNoSpeech()
    {
        var engine = Ready();

        var result = await engine.TranscribeAsync(new float[3 * 16000], "", TestContext.Current.CancellationToken);

        output.WriteLine($"Silence transcript: \"{result.Text}\" (no-speech {result.MaxNoSpeechProbability:F3})");
        (new TranscriptCleaner().Process(result.Text) == "" || result.MaxNoSpeechProbability >= 0.6f).ShouldBeTrue(
            $"\"{result.Text}\" with no-speech {result.MaxNoSpeechProbability:F3}");
    }

    // Whisper answers "Thank you." for zeros and Whisper.net always reports a no-speech probability of 0, so
    // the engine itself has to recognise a dead input.
    [Fact]
    public async Task Transcribe_DigitalSilence_ReturnsNoSpeechWithoutRunningTheModel()
    {
        var engine = Ready();
        var stopwatch = Stopwatch.StartNew();

        var result = await engine.TranscribeAsync(new float[3 * 16000], "", TestContext.Current.CancellationToken);

        output.WriteLine($"Digital silence answered in {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        result.Text.ShouldBe("");
        result.MaxNoSpeechProbability.ShouldBe(1f);
    }

    [Fact]
    public async Task Transcribe_FiveSecondSentence_UnderOneSecond_OnCuda()
    {
        var engine = Ready();
        var samples = SynthesizeFiveSeconds();

        var stopwatch = Stopwatch.StartNew();
        var result = await engine.TranscribeAsync(samples, "", TestContext.Current.CancellationToken);
        var elapsed = stopwatch.Elapsed;

        output.WriteLine($"{samples.Length / 16000.0:F1} s of audio transcribed in {elapsed.TotalMilliseconds:F0} ms on {engine.Status.Runtime}: \"{result.Text}\"");
        Assert.SkipUnless(engine.Status.Runtime == "Cuda", "not on CUDA");
        elapsed.TotalMilliseconds.ShouldBeLessThan(1000);
    }

    [Fact]
    public async Task Transcribe_BeforeInitialize_Throws()
    {
        using var http = new HttpClient();
        using var engine = new WhisperEngine(new ModelManager(MurmurPaths.Default, http, TimeProvider.System, new DiskSpace()));

        engine.Status.State.ShouldBe(EngineState.NotStarted);
        await Should.ThrowAsync<InvalidOperationException>(
            () => engine.TranscribeAsync(new float[16000], "", TestContext.Current.CancellationToken));
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

/// <summary>Status transitions of <see cref="WhisperEngine"/> with a fake model manager, so no real model is involved.</summary>
public sealed class WhisperEngineStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "murmur-engine-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static WhisperEngine CreateEngine(Func<IProgress<DownloadProgress>, CancellationToken, Task<string>> ensure) =>
        new(new FakeModelManager(ensure));

    [Fact]
    public async Task Initialize_DownloadFails_EndsInErrorAndDoesNotThrow()
    {
        using var engine = CreateEngine((_, _) =>
            throw new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound));
        var states = Record(engine);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        engine.Status.State.ShouldBe(EngineState.Error);
        engine.Status.Error.ShouldNotBeNull().ShouldContain("download the speech model");
        engine.Status.Error.ShouldContain("internet connection");
        states.Select(s => s.State).ShouldBe([EngineState.Downloading, EngineState.Error]);
        await Should.ThrowAsync<InvalidOperationException>(
            () => engine.TranscribeAsync(new float[16000], "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Initialize_CorruptModel_ReportsThrottledProgressThenLoadError()
    {
        var body = new byte[2000];
        Random.Shared.NextBytes(body);
        using var engine = CreateEngine(async (progress, ct) =>
        {
            // One report per byte, like a slow connection, so every read is a progress report.
            for (var received = 1; received <= body.Length; received++)
                progress.Report(new DownloadProgress("Whisper large-v3-turbo", received, body.Length, 0, null, 0));

            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, "corrupt.bin");
            await File.WriteAllBytesAsync(path, body, ct);
            return path;
        });
        var states = Record(engine);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        var downloads = states.Where(s => s.State == EngineState.Downloading).Select(s => s.DownloadProgress!.Value).ToList();
        downloads.First().ShouldBe(0);
        downloads.Last().ShouldBe(1.0);
        downloads.ShouldBeInOrder(SortDirection.Ascending);
        downloads.Distinct().Count().ShouldBe(downloads.Count, "each whole percent is reported once");
        downloads.Count.ShouldBeLessThanOrEqualTo(101);
        states.Select(s => s.State).Distinct().ShouldBe([EngineState.Downloading, EngineState.Loading, EngineState.Error]);
        engine.Status.Error.ShouldNotBeNull().ShouldContain("load the speech model");
        engine.Status.Error.ShouldContain(Path.Combine(Path.GetTempPath(), "ggml-large-v3-turbo.bin"));
    }

    [Fact]
    public async Task Initialize_Cancelled_DoesNotThrowAndLeavesNotStarted()
    {
        using var engine = CreateEngine(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            throw new InvalidOperationException("not cancelled");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await engine.InitializeAsync(cts.Token);

        engine.Status.State.ShouldBe(EngineState.NotStarted);
        engine.Status.Error.ShouldBeNull();
    }

    private static List<EngineStatus> Record(WhisperEngine engine)
    {
        var states = new List<EngineStatus>();
        engine.StatusChanged += states.Add;
        return states;
    }

    /// <summary>Has no model on disk, and hands the download over to <c>ensure</c>.</summary>
    private sealed class FakeModelManager(Func<IProgress<DownloadProgress>, CancellationToken, Task<string>> ensure) : IModelManager
    {
        public string PathFor(SpeechModelInfo model) => Path.Combine(Path.GetTempPath(), model.FileName);

        public bool HasExpectedFile(SpeechModelInfo model) => false;

        public Task<string> EnsureModelAsync(SpeechModelInfo model, IProgress<DownloadProgress> progress, CancellationToken ct) =>
            ensure(progress, ct);

        public void Delete(SpeechModelInfo model)
        {
        }
    }
}
