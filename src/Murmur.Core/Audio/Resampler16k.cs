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

        // Input driven: tell the resampler how many samples we are about to give it, and it says how many
        // output samples that yields.
        var maxOutput = _resampler.ResamplePrepare(mono.Length, 1, out var inBuffer, out var inOffset);
        mono.CopyTo(inBuffer.AsSpan(inOffset));

        var output = new float[maxOutput];
        var produced = _resampler.ResampleOut(output, 0, mono.Length, maxOutput, 1);
        return produced == output.Length ? output : output[..produced];
    }

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
