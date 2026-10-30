using NAudio.Dsp;

namespace Murmur.Core.Audio;

/// <summary>
/// Streaming resampler from <paramref name="sourceRate"/> to 16 kHz for mono audio. Keeps filter state
/// across <see cref="Process"/> calls, so a recording can be fed in arbitrarily sized chunks.
/// </summary>
public sealed class Resampler16k(int sourceRate)
{
    public const int TargetRate = 16_000;

    // Null when the source is already 16 kHz: nothing to resample.
    private readonly WdlResampler? _resampler = CreateResampler(sourceRate);

    public float[] Process(ReadOnlySpan<float> mono)
    {
        if (mono.IsEmpty)
            return [];

        if (_resampler is null)
            return mono.ToArray();

        // Input driven: ResamplePrepare takes (and returns) the number of input samples we are about to give it.
        var inputCount = _resampler.ResamplePrepare(mono.Length, 1, out var inBuffer, out var inOffset);
        mono.CopyTo(inBuffer.AsSpan(inOffset));

        // The output room must be sized for the rate change: upsampling (an 8 kHz Bluetooth headset) yields more
        // samples than it is given, and ResampleOut stops at the capacity it is told about.
        var output = new float[MaxOutputFor(inputCount)];
        var produced = _resampler.ResampleOut(output, 0, inputCount, output.Length, 1);
        return produced == output.Length ? output : output[..produced];
    }

    /// <summary>The most output samples <paramref name="inputCount"/> input samples can yield, with headroom for the carried fractional position.</summary>
    private int MaxOutputFor(int inputCount) => (int)Math.Ceiling((double)inputCount * TargetRate / sourceRate) + 8;

    private static WdlResampler? CreateResampler(int sourceRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceRate, 0);

        if (sourceRate == TargetRate)
            return null;

        var resampler = new WdlResampler();
        resampler.SetMode(interp: true, filtercnt: 1, sinc: true);
        resampler.SetFilterParms();
        resampler.SetFeedMode(wantInputDriven: true);
        resampler.SetRates(sourceRate, TargetRate);
        return resampler;
    }
}
