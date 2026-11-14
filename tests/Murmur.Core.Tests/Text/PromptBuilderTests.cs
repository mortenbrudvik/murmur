using Murmur.Core.Text;
using Shouldly;

namespace Murmur.Core.Tests.Text;

public sealed class PromptBuilderTests
{
    [Fact]
    public void Build_NoWords_ReturnsEmpty()
    {
        PromptBuilder.Build([]).ShouldBe("");
    }

    [Fact]
    public void Build_OnlyBlankWords_ReturnsEmpty()
    {
        PromptBuilder.Build(["", "  "]).ShouldBe("");
    }

    [Fact]
    public void Build_TrimsDedupesCaseInsensitivelyAndDropsBlanks()
    {
        PromptBuilder.Build(["Murmur", " WebView2 ", "murmur", ""]).ShouldBe("Murmur, WebView2.");
    }

    [Fact]
    public void Build_ManyWords_StaysWithinLimitAndNeverSplitsAWord()
    {
        var words = Enumerable.Range(0, 500).Select(i => $"word{i}").ToList();

        var prompt = PromptBuilder.Build(words);

        prompt.Length.ShouldBeLessThanOrEqualTo(800);
        prompt.ShouldEndWith(".");
        var included = prompt[..^1].Split(", ");
        included.Length.ShouldBeGreaterThan(1);
        included.ShouldBe(words.Take(included.Length));
    }

    [Theory]
    [InlineData(799, true)] // 799 + "." is exactly MaxLength
    [InlineData(800, false)] // one past it: left out whole, never cut
    public void Build_OneWordAtTheLimit_IsKeptOnlyIfThePromptFits(int wordLength, bool kept)
    {
        var word = new string('a', wordLength);

        var prompt = PromptBuilder.Build([word]);

        prompt.ShouldBe(kept ? word + "." : "");
        prompt.Length.ShouldBeLessThanOrEqualTo(PromptBuilder.MaxLength);
    }

    [Theory]
    [InlineData(397, true)] // 400 + ", " + 397 + "." is exactly MaxLength
    [InlineData(398, false)]
    public void Build_SecondWordAtTheLimit_CountsTheSeparator(int secondLength, bool kept)
    {
        var first = new string('a', 400);
        var second = new string('b', secondLength);

        var prompt = PromptBuilder.Build([first, second, "c"]);

        // A word that does not fit ends the prompt: later, shorter words are left out too, so the order is kept.
        prompt.ShouldBe(kept ? $"{first}, {second}." : $"{first}.");
        prompt.Length.ShouldBe(kept ? PromptBuilder.MaxLength : 401);
    }
}
