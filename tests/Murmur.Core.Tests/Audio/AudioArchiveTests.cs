using Murmur.Core.Audio;
using Murmur.Core.Services;
using NAudio.Wave;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

public sealed class AudioArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "murmur-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MurmurPaths _paths;

    public AudioArchiveTests() => _paths = new MurmurPaths(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WavPath(Guid id) => Path.Combine(_paths.AudioDirectory, id + ".wav");

    [Fact]
    public void Save_Writes16kMonoWav()
    {
        var id = Guid.NewGuid();

        new AudioArchive(_paths).Save(id, new float[16000]);

        using var reader = new WaveFileReader(WavPath(id));
        reader.WaveFormat.SampleRate.ShouldBe(16000);
        reader.WaveFormat.Channels.ShouldBe(1);
        reader.WaveFormat.BitsPerSample.ShouldBe(16);
        reader.TotalTime.TotalSeconds.ShouldBe(1.0, tolerance: 0.01);
    }

    [Fact]
    public void Save_ConvertsFloatToPcm16_ClampingOutOfRange()
    {
        var id = Guid.NewGuid();

        new AudioArchive(_paths).Save(id, [0f, 0.5f, -0.5f, 2f, -2f]);

        using var reader = new WaveFileReader(WavPath(id));
        var bytes = new byte[10];
        reader.ReadExactly(bytes);
        var samples = Enumerable.Range(0, 5).Select(i => (int)BitConverter.ToInt16(bytes, i * 2)).ToArray();
        samples[0].ShouldBe(0);
        samples[1].ShouldBeInRange(16383, 16384);
        samples[2].ShouldBeInRange(-16384, -16383);
        samples[3].ShouldBe(short.MaxValue);
        samples[4].ShouldBeInRange(-32768, -32767);
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var archive = new AudioArchive(_paths);
        var id = Guid.NewGuid();
        archive.Save(id, new float[160]);

        archive.Delete(id);

        File.Exists(WavPath(id)).ShouldBeFalse();
    }

    [Fact]
    public void Delete_MissingFile_DoesNotThrow()
    {
        Should.NotThrow(() => new AudioArchive(_paths).Delete(Guid.NewGuid()));
    }

    [Fact]
    public void Clear_RemovesAllWavs()
    {
        var archive = new AudioArchive(_paths);
        archive.Save(Guid.NewGuid(), new float[160]);
        archive.Save(Guid.NewGuid(), new float[160]);

        archive.Clear();

        Directory.GetFiles(_paths.AudioDirectory, "*.wav").ShouldBeEmpty();
    }

    [Fact]
    public void Clear_MissingDirectory_DoesNotThrow()
    {
        Should.NotThrow(() => new AudioArchive(_paths).Clear());
    }
}
