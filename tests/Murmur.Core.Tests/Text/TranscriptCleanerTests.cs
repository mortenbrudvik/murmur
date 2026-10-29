using Murmur.Core.Text;
using Shouldly;

namespace Murmur.Core.Tests.Text;

public sealed class TranscriptCleanerTests
{
    private readonly TranscriptCleaner _cleaner = new();

    [Fact]
    public void Order_IsZero() => _cleaner.Order.ShouldBe(0);

    [Theory]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData(" (silence) ", "")]
    [InlineData("*music*", "")]
    [InlineData("  Hello   there.  ", "Hello there.")]
    [InlineData("Hello [BLANK_AUDIO] world", "Hello world")]
    [InlineData("See (below) now", "See (below) now")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("[BLANK_AUDIO] (music)", "")]
    [InlineData("Line one\n\nline   two\t!", "Line one line two !")]
    public void Process_CleansWhisperArtifacts(string input, string expected)
    {
        _cleaner.Process(input).ShouldBe(expected);
    }

    /// <summary>Whisper turns near-silence into a lone "." or "…"; text without a letter or digit is not speech.</summary>
    [Theory]
    [InlineData(".", "")]
    [InlineData("…", "")]
    [InlineData(" ?! ", "")]
    [InlineData("[BLANK_AUDIO] .", "")]
    [InlineData("Hi.", "Hi.")]
    [InlineData("3.", "3.")]
    [InlineData("¿Sí?", "¿Sí?")]
    public void Process_TextWithoutLettersOrDigits_IsEmpty(string input, string expected)
    {
        _cleaner.Process(input).ShouldBe(expected);
    }
}
