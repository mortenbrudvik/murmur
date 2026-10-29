namespace Murmur.Core.Audio;

/// <summary>
/// Energy-based speech detector for live 16 kHz mono audio. Input is split into fixed 30 ms frames
/// (leftover samples are carried into the next <see cref="Process"/> call); speech is declared once at
/// least <c>minSpeechFrames</c> frames, not necessarily consecutive, have an RMS above <c>thresholdDbfs</c>.
/// </summary>
public sealed class SpeechDetector(float thresholdDbfs = -40f, int minSpeechFrames = 5)
{
    /// <summary>Samples per frame: 30 ms at 16 kHz.</summary>
    private const int FrameSize = 480;

    private const float FloorDbfs = -120f;

    private readonly float[] _carry = new float[FrameSize];
    private int _carryCount;
    private int _speechFrames;

    public bool SpeechDetected { get; private set; }

    public void Process(ReadOnlySpan<float> samples)
    {
        if (SpeechDetected)
            return;

        // Complete a partially filled frame carried over from the previous call.
        if (_carryCount > 0)
        {
            var take = Math.Min(FrameSize - _carryCount, samples.Length);
            samples[..take].CopyTo(_carry.AsSpan(_carryCount));
            _carryCount += take;
            samples = samples[take..];

            if (_carryCount < FrameSize)
                return;

            _carryCount = 0;
            EvaluateFrame(_carry);
        }

        while (!SpeechDetected && samples.Length >= FrameSize)
        {
            EvaluateFrame(samples[..FrameSize]);
            samples = samples[FrameSize..];
        }

        // A partial final frame is not evaluated; keep it so frames line up with the next call.
        if (!SpeechDetected && !samples.IsEmpty)
        {
            samples.CopyTo(_carry);
            _carryCount = samples.Length;
        }
    }

    public void Reset()
    {
        _carryCount = 0;
        _speechFrames = 0;
        SpeechDetected = false;
    }

    /// <summary>RMS level in dBFS (20·log10(rms)), floored at -120; empty or silent input yields the floor.</summary>
    public static float RmsDbfs(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return FloorDbfs;

        double sumOfSquares = 0;
        foreach (var sample in samples)
            sumOfSquares += (double)sample * sample;

        var rms = Math.Sqrt(sumOfSquares / samples.Length);
        if (rms <= 0)
            return FloorDbfs;

        return (float)Math.Max(FloorDbfs, 20 * Math.Log10(rms));
    }

    private void EvaluateFrame(ReadOnlySpan<float> frame)
    {
        if (RmsDbfs(frame) <= thresholdDbfs)
            return;

        if (++_speechFrames >= minSpeechFrames)
            SpeechDetected = true;
    }
}
