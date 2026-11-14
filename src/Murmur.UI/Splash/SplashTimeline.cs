namespace Murmur.UI.Splash;

/// <summary>
/// The beats of the splash as functions of the time since it appeared, in seconds. Everything the splash draws is
/// decided here, so one clock drives it and each beat can be tested without a window. The values are the design's
/// (docs/superpowers/specs/2026-11-14-splash-about-design.md §5.1); the mock-up's script is their reference.
/// </summary>
internal static class SplashTimeline
{
    /// <summary>By now everything has come to rest: the final frame.</summary>
    internal const double End = 4.3;

    internal const string Word = "Murmur";

    private const double MorphStart = 0.9;
    private const double MorphLength = 0.75;
    private const double WaveSwell = 0.25;
    private const double TileStart = 1.45;
    private const double TileLength = 0.45;
    private const double StatusStart = 0.3;
    private const double StatusLength = 0.3;
    // The name is typed while the tile finishes lighting up, 0.2 s earlier than in the mock-up: the splash can close at
    // 2.5 s, and the finished name should have stood there for a moment by then (it ends at 2.175 s).
    private const double CaretStart = 1.6;
    private const double TypingStart = 1.75;
    private const double LetterEvery = 0.085;
    private const double BlinkEvery = 0.4;
    /// <summary>How long the caret blinks after the last letter, on a start slow enough to see it.</summary>
    private const double BlinkFor = 1.2;

    /// <summary>When the last letter appears.</summary>
    private static readonly double TypingEnd = TypingStart + (Word.Length - 1) * LetterEvery;

    /// <summary>How far the line has gone from a speech waveform (0) to the icon's line (1).</summary>
    internal static double Morph(double time) => Ease(Clamp((time - MorphStart) / MorphLength));

    /// <summary>How loud the speech waveform is: it swells in at the start and dies down as the line settles.</summary>
    internal static double WaveAmplitude(double time) => Clamp(time / WaveSwell) * (1 - Morph(time));

    /// <summary>How far the tile has lit up around the m.</summary>
    internal static double TileReveal(double time) => Ease(Clamp((time - TileStart) / TileLength));

    /// <summary>The status line and the version fade in early and stay.</summary>
    internal static double StatusOpacity(double time) => Clamp((time - StatusStart) / StatusLength);

    /// <summary>How many letters of the name have been typed.</summary>
    internal static int TypedLetters(double time) =>
        time < TypingStart ? 0 : Math.Min(Word.Length, (int)Math.Floor((time - TypingStart) / LetterEvery) + 1);

    /// <summary>The caret is there while the name is typed, then blinks for a moment and goes.</summary>
    internal static bool CaretVisible(double time)
    {
        if (time < CaretStart)
            return false;
        if (time < TypingEnd)
            return true;

        var sinceTyped = time - TypingEnd;
        return sinceTyped < BlinkFor && (int)Math.Floor(sinceTyped / BlinkEvery) % 2 == 0;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    /// <summary>Cubic ease in and out.</summary>
    private static double Ease(double value) =>
        value < 0.5 ? 4 * value * value * value : 1 - Math.Pow(-2 * value + 2, 3) / 2;
}
