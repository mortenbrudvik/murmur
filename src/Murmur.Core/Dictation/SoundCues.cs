using System.Media;
using NAudio.Wave;
using Serilog;

namespace Murmur.Core.Dictation;

/// <summary>
/// Two soft 60 ms sine blips: 880 Hz when the microphone goes live, 660 Hz when recording stops. Each is generated
/// once as an in-memory 16-bit WAV and played asynchronously with <see cref="SoundPlayer"/>.
/// </summary>
public sealed class SoundCues : ISoundCues, IDisposable
{
    private const int SampleRate = 48_000;
    private const int DurationMs = 60;
    private const int FadeMs = 10;
    private const double Amplitude = 0.15;
    private const double StartFrequencyHz = 880;
    private const double StopFrequencyHz = 660;

    private readonly ILogger _logger = Log.ForContext<SoundCues>();
    private readonly SoundPlayer _start = CreatePlayer(StartFrequencyHz);
    private readonly SoundPlayer _stop = CreatePlayer(StopFrequencyHz);

    public void PlayStart() => Play(_start);

    public void PlayStop() => Play(_stop);

    public void Dispose()
    {
        _start.Dispose();
        _stop.Dispose();
    }

    /// <summary>A mono 16-bit WAV of a sine at <see cref="Amplitude"/>, with linear fades at both ends (no clicks).</summary>
    internal static byte[] CreateWav(double frequencyHz)
    {
        const int sampleCount = SampleRate * DurationMs / 1000;
        const int fadeSamples = SampleRate * FadeMs / 1000;

        var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, new WaveFormat(SampleRate, 16, 1)))
        {
            for (var i = 0; i < sampleCount; i++)
            {
                var envelope = Math.Min(1.0, Math.Min(i, sampleCount - 1 - i) / (double)fadeSamples);
                writer.WriteSample((float)(Amplitude * envelope * Math.Sin(2 * Math.PI * frequencyHz * i / SampleRate)));
            }
        }

        // Disposing the writer finalises the header and closes the stream; ToArray still works on a closed stream.
        return stream.ToArray();
    }

    private static SoundPlayer CreatePlayer(double frequencyHz)
    {
        var player = new SoundPlayer(new MemoryStream(CreateWav(frequencyHz)));
        player.Load();
        return player;
    }

    /// <summary>
    /// Plays on a pool thread with <see cref="SoundPlayer.PlaySync"/> rather than <see cref="SoundPlayer.Play"/>:
    /// Play hands PlaySound(SND_MEMORY | SND_ASYNC) the player's managed buffer, which is pinned only for the duration
    /// of that call, so a compacting GC during playback could move it. PlaySync keeps it pinned until the end.
    /// </summary>
    private void Play(SoundPlayer player) =>
        _ = Task.Run(() =>
        {
            try
            {
                player.PlaySync();
            }
            catch (Exception ex)
            {
                // A cue is a nicety: a missing or failing output device must not break dictation.
                _logger.Warning(ex, "Could not play a sound cue");
            }
        });
}
