using System.Buffers.Binary;
using Murmur.Core.Services;
using NAudio.Wave;

namespace Murmur.Core.Audio;

public sealed class AudioArchive(MurmurPaths paths) : IAudioArchive
{
    private const string Extension = ".wav";
    private const string TempExtension = ".tmp";

    private static readonly WaveFormat Format = new(16000, 16, 1);

    public void Save(Guid id, float[] samples16kMono)
    {
        Directory.CreateDirectory(paths.AudioDirectory);

        var path = PathFor(id);
        var tempPath = path + TempExtension;
        using (var writer = new WaveFileWriter(tempPath, Format))
            writer.Write(ToPcm16(samples16kMono));

        // Renamed into place so a crash cannot leave a truncated .wav that looks like a real recording.
        File.Move(tempPath, path, overwrite: true);
    }

    public void Delete(Guid id)
    {
        var path = PathFor(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void Clear()
    {
        if (!Directory.Exists(paths.AudioDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(paths.AudioDirectory))
        {
            // Includes leftovers from an interrupted Save. EnumerateFiles patterns match loosely
            // ("*.wav" also matches "x.wavx"), so the extension is compared exactly instead.
            var name = Path.GetFileName(file);
            if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(Extension + TempExtension, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    private string PathFor(Guid id) => Path.Combine(paths.AudioDirectory, id + Extension);

    private static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = float.IsNaN(samples[i]) ? 0f : Math.Clamp(samples[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)MathF.Round(sample * short.MaxValue));
        }

        return bytes;
    }
}
