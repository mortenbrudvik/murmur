using Murmur.Core.Features.Vocabulary;
using Murmur.Core.Models;
using Murmur.Core.Services;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class VocabularyHandlerTests
{
    private readonly IVocabularyService _vocabulary = Substitute.For<IVocabularyService>();

    [Fact]
    public async Task GetVocabulary_ReturnsTheStoredVocabulary()
    {
        var stored = new Vocabulary(["Murmur"], [new ReplacementRule("web view", "WebView")]);
        _vocabulary.Load().Returns(stored);

        var result = await new GetVocabularyHandler(_vocabulary).Handle(new GetVocabularyRequest(), CancellationToken.None);

        result.ShouldBe(stored);
    }

    [Fact]
    public async Task UpdateVocabulary_Normalizes()
    {
        Vocabulary? saved = null;
        _vocabulary.When(v => v.Save(Arg.Any<Vocabulary>())).Do(call => saved = call.Arg<Vocabulary>());

        var result = await new UpdateVocabularyHandler(_vocabulary).Handle(
            new UpdateVocabularyRequest(
                ["  Murmur ", "", "murmur", "WPF"],
                [new ReplacementRule(" ", "x"), new ReplacementRule("web view", "WebView")]),
            CancellationToken.None);

        saved.ShouldNotBeNull();
        saved.HintWords.ShouldBe(["Murmur", "WPF"]);
        saved.Replacements.ShouldBe([new ReplacementRule("web view", "WebView")]);
        result.ShouldBe(saved);
    }

    [Fact]
    public async Task UpdateVocabulary_FirstSpellingWinsAndWhitespaceOnlyWordsAreDropped()
    {
        Vocabulary? saved = null;
        _vocabulary.When(v => v.Save(Arg.Any<Vocabulary>())).Do(call => saved = call.Arg<Vocabulary>());

        await new UpdateVocabularyHandler(_vocabulary).Handle(
            new UpdateVocabularyRequest(["WPF", "  ", "\t", "wpf", " Wpf "], []),
            CancellationToken.None);

        saved.ShouldNotBeNull();
        saved.HintWords.ShouldBe(["WPF"]);
    }

    [Fact]
    public async Task UpdateVocabulary_TrimsSpokenButKeepsWrittenAsTyped()
    {
        Vocabulary? saved = null;
        _vocabulary.When(v => v.Save(Arg.Any<Vocabulary>())).Do(call => saved = call.Arg<Vocabulary>());

        await new UpdateVocabularyHandler(_vocabulary).Handle(
            new UpdateVocabularyRequest([], [new ReplacementRule("  em dash ", " — ")]),
            CancellationToken.None);

        saved.ShouldNotBeNull();
        saved.Replacements.ShouldBe([new ReplacementRule("em dash", " — ")]);
    }
}
