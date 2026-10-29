using Murmur.Core.Audio;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

public sealed class SpeechDetectorTests
{
    private const int SampleRate = 16_000;

    private static float[] Sine(float amplitude, double hz, int ms)
    {
        var count = SampleRate * ms / 1000;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / SampleRate));
        return samples;
    }

    [Fact]
    public void Zeros_OneSecond_NoSpeech()
    {
        var detector = new SpeechDetector();

        detector.Process(new float[SampleRate]);

        detector.SpeechDetected.ShouldBeFalse();
    }

    [Fact]
    public void QuietNoise_OneSecond_NoSpeech()
    {
        var random = new Random(42);
        var noise = new float[SampleRate];
        for (var i = 0; i < noise.Length; i++)
            noise[i] = (float)((random.NextDouble() * 2 - 1) * 0.005);
        var detector = new SpeechDetector();

        detector.Process(noise);

        detector.SpeechDetected.ShouldBeFalse();
    }

    [Fact]
    public void Sine_300ms_Amp0_1_IsSpeech()
    {
        var detector = new SpeechDetector();

        detector.Process(Sine(0.1f, 440, 300));

        detector.SpeechDetected.ShouldBeTrue();
    }

    [Fact]
    public void Sine_120ms_Amp0_1_TooShort()
    {
        var detector = new SpeechDetector();

        detector.Process(Sine(0.1f, 440, 120));

        detector.SpeechDetected.ShouldBeFalse();
    }

    [Fact]
    public void Sine_200ms_FedIn7SampleChunks_IsSpeech()
    {
        var samples = Sine(0.1f, 440, 200);
        var detector = new SpeechDetector();

        for (var offset = 0; offset < samples.Length; offset += 7)
            detector.Process(samples.AsSpan(offset, Math.Min(7, samples.Length - offset)));

        detector.SpeechDetected.ShouldBeTrue();
    }

    [Fact]
    public void Reset_ClearsDetection()
    {
        var detector = new SpeechDetector();
        detector.Process(Sine(0.1f, 440, 300));
        detector.SpeechDetected.ShouldBeTrue();

        detector.Reset();

        detector.SpeechDetected.ShouldBeFalse();
    }

    [Fact]
    public void RmsDbfs_FullScaleSine_IsMinus3()
    {
        SpeechDetector.RmsDbfs(Sine(1f, 440, 1000)).ShouldBe(-3.01f, 0.05f);
    }

    [Fact]
    public void RmsDbfs_Zeros_IsFloor()
    {
        SpeechDetector.RmsDbfs(new float[480]).ShouldBe(-120f);
    }
}
