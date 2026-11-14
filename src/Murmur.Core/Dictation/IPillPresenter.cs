namespace Murmur.Core.Dictation;

/// <summary>What a pill notice is about, which decides how long it stays.</summary>
public enum NoticeKind
{
    /// <summary>Passing news that asks nothing of the user ("Loading speech model…"). Shown for about 1.5 s.</summary>
    Info,

    /// <summary>
    /// Something went wrong, or the user has to act: the text was not pasted, or nothing could be dictated. Shown for
    /// about 4.5 s, long enough to read and act on.
    /// </summary>
    Problem,
}

/// <summary>
/// The small recording overlay ("pill") that is the only UI shown while dictating. Implementations never take
/// focus and may be called from any thread; calls are applied in the order they were made.
/// </summary>
public interface IPillPresenter
{
    /// <summary>
    /// Shows the pill while the microphone is being started (a hollow dot and flat, dimmed bars): the hotkey was
    /// seen, but nothing is recorded yet.
    /// </summary>
    void ShowStarting();

    /// <summary>Shows the pill with a live waveform (a red dot and level bars): the microphone is recording.</summary>
    void ShowListening();

    /// <summary>Adds one microphone level reading to the waveform.</summary>
    /// <param name="rmsDbfs">RMS level in dBFS; 0 is full scale, silence is very negative.</param>
    void PushLevel(float rmsDbfs);

    /// <summary>Shows the pill with three pulsing dots (the audio is being transcribed).</summary>
    void ShowTranscribing();

    /// <summary>
    /// Shows a short message, announces it to screen readers, then hides the pill by itself after a time that depends
    /// on <paramref name="kind"/>.
    /// </summary>
    void ShowNotice(string text, NoticeKind kind);

    /// <summary>Hides the pill.</summary>
    void Hide();
}
