using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Text;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Text;

public sealed class VocabularyReplacerTests
{
    [Theory]
    [InlineData("I use web view two daily", "web view two", "WebView2", "I use WebView2 daily")]
    [InlineData("Web View Two.", "web view two", "WebView2", "WebView2.")]
    [InlineData("concatenate", "cat", "dog", "concatenate")]
    [InlineData("I write c sharp", "c sharp", "C#", "I write C#")]
    [InlineData("a dollar sign here", "dollar sign", "$", "a $ here")]
    [InlineData("say one", "one", "$1", "say $1")]
    [InlineData("aXb", "a.b", "X", "aXb")]
    [InlineData("a.b and aXb", "a.b", "X", "X and aXb")]
    [InlineData("the cat sat. Cat!", "cat", "dog", "the dog sat. dog!")]
    public void ApplyRules_SingleRule(string text, string spoken, string written, string expected)
    {
        VocabularyReplacer.ApplyRules(text, [new ReplacementRule(spoken, written)]).ShouldBe(expected);
    }

    [Fact]
    public void ApplyRules_LongestPhraseWinsRegardlessOfRuleOrder()
    {
        ReplacementRule[] rules = [new("web view", "WebView"), new("web view two", "WebView2")];

        VocabularyReplacer.ApplyRules("web view two", rules).ShouldBe("WebView2");
        VocabularyReplacer.ApplyRules("web view two", [.. rules.Reverse()]).ShouldBe("WebView2");
    }

    [Fact]
    public void ApplyRules_NoRules_ReturnsTextUnchanged()
    {
        VocabularyReplacer.ApplyRules("unchanged", []).ShouldBe("unchanged");
    }

    [Fact]
    public void ApplyRules_BlankSpoken_IsIgnored()
    {
        ReplacementRule[] rules = [new("", "X"), new("   ", "Y")];

        VocabularyReplacer.ApplyRules("hello world", rules).ShouldBe("hello world");
    }

    [Fact]
    public void Order_Is100() => new VocabularyReplacer(Substitute.For<IVocabularyService>()).Order.ShouldBe(100);

    [Fact]
    public void Process_AppliesTheCurrentVocabularyRules()
    {
        var vocabulary = Substitute.For<IVocabularyService>();
        vocabulary.Load().Returns(new Vocabulary([], [new ReplacementRule("web view two", "WebView2")]));
        var replacer = new VocabularyReplacer(vocabulary);

        replacer.Process("I like web view two").ShouldBe("I like WebView2");

        vocabulary.Load().Returns(new Vocabulary([], [new ReplacementRule("web view two", "WV2")]));

        replacer.Process("I like web view two").ShouldBe("I like WV2");
    }
}
