using Murmur.Core.Audio;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

public sealed class Resampler16kTests
{
    private static float[] Sine(int sampleRate, double hz, float amplitude, int count)
    {
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / sampleRate));
        return samples;
    }

    private static float[] ProcessInChunks(Resampler16k resampler, float[] input, int chunks)
    {
        var output = new List<float>();
        var chunkSize = input.Length / chunks;
        for (var i = 0; i < chunks; i++)
        {
            var start = i * chunkSize;
            var length = i == chunks - 1 ? input.Length - start : chunkSize;
            output.AddRange(resampler.Process(input.AsSpan(start, length)));
        }

        return [.. output];
    }

    private static double Rms(float[] samples) => Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);

    [Fact]
    public void Resampler_48kTo16k_LengthAndLevel()
    {
        var resampler = new Resampler16k(48000);

        var output = ProcessInChunks(resampler, Sine(48000, 1000, 0.5f, 48000), chunks: 10);

        output.Length.ShouldBeInRange(16000 - 64, 16000 + 64);
        Rms(output).ShouldBe(0.5 / Math.Sqrt(2), tolerance: 0.5 / Math.Sqrt(2) * 0.05);
    }

    [Fact]
    public void Resampler_44k1To16k_LengthAndLevel()
    {
        var resampler = new Resampler16k(44100);

        var output = ProcessInChunks(resampler, Sine(44100, 1000, 0.5f, 44100), chunks: 10);

        output.Length.ShouldBeInRange(16000 - 64, 16000 + 64);
        Rms(output).ShouldBe(0.5 / Math.Sqrt(2), tolerance: 0.5 / Math.Sqrt(2) * 0.05);
    }

    [Fact]
    public void Resampler_ToneAboveNewNyquist_IsAttenuated()
    {
        // 12 kHz is above the 8 kHz Nyquist of 16 kHz audio; without low-pass filtering it would alias to 4 kHz.
        var resampler = new Resampler16k(48000);

        var output = ProcessInChunks(resampler, Sine(48000, 12000, 0.5f, 48000), chunks: 10);

        Rms(output).ShouldBeLessThan(0.5 / Math.Sqrt(2) * 0.1);
    }

    [Fact]
    public void Resampler_16kSource_PassesThroughLength()
    {
        var resampler = new Resampler16k(16000);

        var output = ProcessInChunks(resampler, Sine(16000, 1000, 0.5f, 16000), chunks: 10);

        output.Length.ShouldBeInRange(16000 - 16, 16000 + 16);
    }

    [Fact]
    public void Resampler_ChunkSizeDoesNotChangeTotalLength()
    {
        var input = Sine(48000, 440, 0.5f, 48000);

        var whole = ProcessInChunks(new Resampler16k(48000), input, chunks: 1);
        var tiny = ProcessInChunks(new Resampler16k(48000), input, chunks: 480);

        Math.Abs(whole.Length - tiny.Length).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Resampler_EmptyInput_ReturnsEmpty()
    {
        new Resampler16k(48000).Process([]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-48000)]
    public void Resampler_InvalidSourceRate_Throws(int sourceRate)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Resampler16k(sourceRate));
    }
}
