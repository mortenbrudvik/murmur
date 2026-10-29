namespace Murmur.Core.Dictation;

/// <summary>
/// The small recording overlay ("pill") that is the only UI shown while dictating. Implementations never take
/// focus and may be called from any thread; calls are applied in the order they were made.
/// </summary>
public interface IPillPresenter
{
    /// <summary>Shows the pill with a live waveform (a red dot and level bars).</summary>
    void ShowListening();

    /// <summary>Adds one microphone level reading to the waveform.</summary>
    /// <param name="rmsDbfs">RMS level in dBFS; 0 is full scale, silence is very negative.</param>
    void PushLevel(float rmsDbfs);

    /// <summary>Shows the pill with three pulsing dots (the audio is being transcribed).</summary>
    void ShowTranscribing();

    /// <summary>Shows a short message, then hides the pill by itself after 1.5 s.</summary>
    void ShowNotice(string text);

    /// <summary>Hides the pill.</summary>
    void Hide();
}
