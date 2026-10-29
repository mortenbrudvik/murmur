using Murmur.Core.Audio;
using NAudio.Wave;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

public sealed class SampleConverterTests
{
    [Fact]
    public void ToMono_Stereo_Averages()
    {
        var mono = SampleConverter.ToMono([1f, 0f, 0f, 1f], 2);

        mono.ShouldBe([0.5f, 0.5f]);
    }

    [Fact]
    public void ToMono_Mono_PassesThrough()
    {
        var mono = SampleConverter.ToMono([0.1f, 0.2f], 1);

        mono.ShouldBe([0.1f, 0.2f]);
    }

    [Fact]
    public void ToMono_Mono_ReturnsACopy()
    {
        float[] source = [0.1f, 0.2f];

        var mono = SampleConverter.ToMono(source, 1);
        source[0] = 0.9f;

        mono[0].ShouldBe(0.1f);
    }

    [Fact]
    public void ToMono_FourChannels_AveragesEachFrame()
    {
        var mono = SampleConverter.ToMono([1f, 1f, 1f, 1f, 0f, 0.5f, 0f, 0.5f], 4);

        mono.ShouldBe([1f, 0.25f]);
    }

    [Fact]
    public void ToMono_IncompleteTrailingFrame_IsDropped()
    {
        var mono = SampleConverter.ToMono([1f, 0f, 0.8f], 2);

        mono.ShouldBe([0.5f]);
    }

    [Fact]
    public void ToMono_Empty_ReturnsEmpty()
    {
        SampleConverter.ToMono([], 2).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToMono_InvalidChannelCount_Throws(int channels)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SampleConverter.ToMono([0f], channels));
    }

    [Fact]
    public void ToFloat_Float32_ReinterpretsBytes()
    {
        float[] source = [0.25f, -0.5f, 1f];
        var bytes = new byte[source.Length * sizeof(float)];
        Buffer.BlockCopy(source, 0, bytes, 0, bytes.Length);

        var samples = SampleConverter.ToFloat(bytes, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));

        samples.ShouldBe(source);
    }

    [Fact]
    public void ToFloat_Pcm16_ScalesToUnitRange()
    {
        short[] source = [0, 16384, -16384, short.MinValue];
        var bytes = new byte[source.Length * sizeof(short)];
        Buffer.BlockCopy(source, 0, bytes, 0, bytes.Length);

        var samples = SampleConverter.ToFloat(bytes, new WaveFormat(16000, 16, 1));

        samples.ShouldBe([0f, 0.5f, -0.5f, -1f]);
    }

    [Fact]
    public void ToFloat_ExtensibleFloat_IsRecognizedAsFloat()
    {
        float[] source = [0.25f, -0.5f];
        var bytes = new byte[source.Length * sizeof(float)];
        Buffer.BlockCopy(source, 0, bytes, 0, bytes.Length);
        var format = new WaveFormatExtensible(48000, 32, 2);

        SampleConverter.IsSupported(format).ShouldBeTrue();
        SampleConverter.ToFloat(bytes, format).ShouldBe(source);
    }

    [Fact]
    public void ToFloat_TrailingPartialSample_IsDropped()
    {
        var samples = SampleConverter.ToFloat(new byte[3], new WaveFormat(16000, 16, 1));

        samples.Length.ShouldBe(1);
    }

    [Fact]
    public void IsSupported_Pcm24_IsFalse()
    {
        SampleConverter.IsSupported(new WaveFormat(48000, 24, 2)).ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => SampleConverter.ToFloat(new byte[6], new WaveFormat(48000, 24, 2)));
    }
}
