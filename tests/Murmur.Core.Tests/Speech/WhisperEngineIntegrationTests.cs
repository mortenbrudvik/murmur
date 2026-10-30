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
/// Loads the real model on the real GPU, built from the same parts as the app: the app's own settings and models
/// folder (<see cref="MurmurPaths.Default"/>), GPU detection, the CUDA runtime search and Whisper.net. The model is the
/// one the settings choose, which a PC that already has the large model keeps; like the app, it saves that choice when
/// the settings have none yet. A PC with no model downloads it once (about 1.6 GB for the large one), so later runs and
/// the app itself reuse it. Like the app, starting the engine deletes every other catalog model's files in that real
/// models folder once the chosen one is Ready, so a PC that keeps both models loses the one the settings do not choose.
/// </summary>
public sealed class WhisperEngineFixture : IAsyncLifetime
{
    private readonly HttpClient _http = new();

    public WhisperEngine Engine { get; private set; } = null!;
    public TimeSpan InitializeElapsed { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var paths = MurmurPaths.Default;
        var settings = new SettingsService(paths, new Win32KeyboardLayout());
        var models = new ModelManager(paths, _http, TimeProvider.System, new DiskSpace());
        var gpu = new GpuInfo(paths);
        Engine = new WhisperEngine(models, new ModelSelection(settings, gpu, models), gpu, new CudaRuntimeLocator(paths),
            new WhisperModelLoader(), settings, TimeProvider.System);
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
    public void Initialize_OnCuda_ReportsCudaInUse()
    {
        var status = Ready().Status;

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
