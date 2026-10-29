using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Murmur.Core.Audio;

public static class SampleConverter
{
    /// <summary>Averages interleaved channels into one mono stream. A trailing incomplete frame is dropped.</summary>
    public static float[] ToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        if (channels == 1)
            return interleaved.ToArray();

        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            foreach (var sample in interleaved.Slice(frame * channels, channels))
                sum += sample;
            mono[frame] = sum / channels;
        }

        return mono;
    }

    /// <summary>Whether <see cref="ToFloat"/> can decode <paramref name="format"/>: 32-bit float or 16-bit PCM.</summary>
    public static bool IsSupported(WaveFormat format)
    {
        var (encoding, bits) = Classify(format);
        return (encoding == WaveFormatEncoding.IeeeFloat && bits == 32)
            || (encoding == WaveFormatEncoding.Pcm && bits == 16);
    }

    /// <summary>
    /// Decodes raw little-endian capture bytes (still interleaved) into floats in the -1..1 range.
    /// A trailing partial sample is dropped.
    /// </summary>
    /// <exception cref="NotSupportedException">The format is not 32-bit float or 16-bit PCM.</exception>
    public static float[] ToFloat(ReadOnlySpan<byte> bytes, WaveFormat format)
    {
        var (encoding, bits) = Classify(format);

        if (encoding == WaveFormatEncoding.IeeeFloat && bits == 32)
        {
            var floats = new float[bytes.Length / sizeof(float)];
            bytes[..(floats.Length * sizeof(float))].CopyTo(MemoryMarshal.AsBytes(floats.AsSpan()));
            return floats;
        }

        if (encoding == WaveFormatEncoding.Pcm && bits == 16)
        {
            var samples = new float[bytes.Length / sizeof(short)];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * sizeof(short))..]) / 32768f;
            return samples;
        }

        throw new NotSupportedException($"Unsupported capture format: {format.Encoding}, {format.BitsPerSample}-bit.");
    }

    /// <summary>Resolves WAVE_FORMAT_EXTENSIBLE (what WASAPI mix formats usually are) to its real encoding.</summary>
    private static (WaveFormatEncoding Encoding, int Bits) Classify(WaveFormat format) =>
        format is WaveFormatExtensible extensible
            ? (extensible.ToStandardWaveFormat().Encoding, format.BitsPerSample)
            : (format.Encoding, format.BitsPerSample);
}
