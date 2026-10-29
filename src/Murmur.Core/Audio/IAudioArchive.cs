namespace Murmur.Core.Audio;

/// <summary>Optional on-disk copy of each dictation's audio, stored as <c>{id}.wav</c>.</summary>
public interface IAudioArchive
{
    /// <summary>Writes the recording as a 16 kHz, 16-bit, mono WAV file named after the history entry id.</summary>
    void Save(Guid id, float[] samples16kMono);

    /// <summary>Removes one recording. Does nothing if it was never saved.</summary>
    void Delete(Guid id);

    /// <summary>Removes every recording.</summary>
    void Clear();
}
