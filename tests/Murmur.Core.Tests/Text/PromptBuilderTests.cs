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
}
