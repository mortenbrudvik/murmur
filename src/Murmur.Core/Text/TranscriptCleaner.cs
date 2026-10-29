using System.Text.RegularExpressions;

namespace Murmur.Core.Text;

/// <summary>Strips Whisper's non-speech artifacts: bracketed tags, lone "(..)" / "*..*" tags, stray whitespace.</summary>
public sealed partial class TranscriptCleaner : ITranscriptPostProcessor
{
    public int Order => 0;

    public string Process(string text)
    {
        var cleaned = Whitespace().Replace(BracketTag().Replace(text, " "), " ").Trim();

        return OnlyNonSpeechTag().IsMatch(cleaned) ? "" : cleaned;
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex BracketTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(?:\([^)]*\)|\*[^*]*\*)$")]
    private static partial Regex OnlyNonSpeechTag();
}
