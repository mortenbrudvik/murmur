namespace Murmur.Core.Dictation;

/// <summary>How a dictation ended, as its log line reports it.</summary>
public enum DictationOutcome
{
    /// <summary>Ctrl+V was sent to the target app.</summary>
    Pasted,

    /// <summary>The text was not pasted but is on the clipboard for the owner to paste by hand.</summary>
    LeftOnClipboard,

    /// <summary>The text was not pasted but is in history.</summary>
    SavedOnly,

    /// <summary>Nothing was said, so nothing was pasted or saved.</summary>
    Silence,

    /// <summary>Esc (or a combo key) ended the dictation; any result was discarded.</summary>
    Cancelled,

    /// <summary>
    /// The microphone or the transcription failed, or the text could be neither pasted, saved nor copied.
    /// </summary>
    Failed,
}
