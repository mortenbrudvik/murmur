using Murmur.Core.Dictation;
using NAudio.Wave;
using Shouldly;

namespace Murmur.Core.Tests.Dictation;

public sealed class SoundCuesTests
{
    private const double Amplitude = 0.15;

    [Theory]
    [InlineData(880)]
    [InlineData(660)]
    public void CreateWav_Is60msOf16BitMonoSine(double frequencyHz)
    {
        using var reader = new WaveFileReader(new MemoryStream(SoundCues.CreateWav(frequencyHz)));
        var format = reader.WaveFormat;
        var samples = ReadSamples(reader);

        format.Encoding.ShouldBe(WaveFormatEncoding.Pcm);
        format.BitsPerSample.ShouldBe(16);
        format.Channels.ShouldBe(1);
        samples.Length.ShouldBe(format.SampleRate * 60 / 1000);

        // Two zero crossings per cycle.
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (Math.Sign(samples[i - 1]) != Math.Sign(samples[i]) && samples[i] != 0)
                crossings++;
        }

        Math.Abs(crossings - 2 * frequencyHz * 0.060).ShouldBeLessThanOrEqualTo(2);
    }

    [Theory]
    [InlineData(880)]
    [InlineData(660)]
    public void CreateWav_HasAmplitude015With10msFades(double frequencyHz)
    {
        using var reader = new WaveFileReader(new MemoryStream(SoundCues.CreateWav(frequencyHz)));
        var samples = ReadSamples(reader);
        var perMs = reader.WaveFormat.SampleRate / 1000;

        Peak(samples, 0, samples.Length).ShouldBe(Amplitude, tolerance: 0.002);
        Peak(samples, 10 * perMs, 50 * perMs).ShouldBe(Amplitude, tolerance: 0.002);

        // Linear fades: about a fifth of full level 2 ms from either end, silent at the very ends.
        Peak(samples, 0, 2 * perMs).ShouldBeLessThan(Amplitude * 0.25);
        Peak(samples, 58 * perMs, 60 * perMs).ShouldBeLessThan(Amplitude * 0.25);
        Math.Abs(samples[0]).ShouldBeLessThan(0.001f);
        Math.Abs(samples[^1]).ShouldBeLessThan(0.001f);
    }

    [Fact]
    public void CreateWav_TwoFrequencies_PlaysTwoBlipsInTurn()
    {
        using var reader = new WaveFileReader(new MemoryStream(SoundCues.CreateWav(523, 392)));
        var samples = ReadSamples(reader);
        var blip = reader.WaveFormat.SampleRate * 60 / 1000;

        samples.Length.ShouldBe(2 * blip);
        Math.Abs(Crossings(samples, 0, blip) - 2 * 523 * 0.060).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(Crossings(samples, blip, 2 * blip) - 2 * 392 * 0.060).ShouldBeLessThanOrEqualTo(2);

        // Each blip fades in and out on its own, so the join does not click.
        Math.Abs(samples[blip - 1]).ShouldBeLessThan(0.001f);
        Math.Abs(samples[blip]).ShouldBeLessThan(0.001f);
    }

    private static int Crossings(float[] samples, int from, int to)
    {
        var crossings = 0;
        for (var i = from + 1; i < to; i++)
        {
            if (Math.Sign(samples[i - 1]) != Math.Sign(samples[i]) && samples[i] != 0)
                crossings++;
        }

        return crossings;
    }

    /// <summary>All samples of a mono file, where one sample is one frame.</summary>
    private static float[] ReadSamples(WaveFileReader reader)
    {
        var provider = reader.ToSampleProvider();
        var samples = new List<float>();
        var buffer = new float[1024];
        int read;
        while ((read = provider.Read(buffer)) > 0)
            samples.AddRange(buffer.AsSpan(0, read));
        return [.. samples];
    }

    private static double Peak(float[] samples, int from, int to)
    {
        var peak = 0.0;
        for (var i = from; i < to; i++)
            peak = Math.Max(peak, Math.Abs(samples[i]));
        return peak;
    }
}
