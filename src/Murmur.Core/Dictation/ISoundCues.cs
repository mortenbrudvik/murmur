namespace Murmur.Core.Dictation;

/// <summary>Short audible cues for dictation. Calls return at once; the sound plays in the background.</summary>
public interface ISoundCues
{
    /// <summary>The microphone is live: start talking.</summary>
    void PlayStart();

    /// <summary>Recording has stopped.</summary>
    void PlayStop();
}
