using System.Globalization;
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

    [Fact]
    public async Task UpdateVocabulary_AtEveryLimit_Saves()
    {
        var words = Enumerable.Range(0, UpdateVocabularyRequest.MaxHintWords)
            .Select(i => i.ToString(CultureInfo.InvariantCulture).PadLeft(UpdateVocabularyRequest.MaxHintWordLength, 'w'))
            .ToList();
        var rules = Enumerable.Range(0, UpdateVocabularyRequest.MaxReplacements)
            .Select(_ => new ReplacementRule(
                new string('s', UpdateVocabularyRequest.MaxSpokenLength), new string('w', UpdateVocabularyRequest.MaxWrittenLength)))
            .ToList();

        var result = await new UpdateVocabularyHandler(_vocabulary).Handle(new UpdateVocabularyRequest(words, rules), CancellationToken.None);

        result.HintWords.Count.ShouldBe(UpdateVocabularyRequest.MaxHintWords);
        result.Replacements.Count.ShouldBe(UpdateVocabularyRequest.MaxReplacements);
        _vocabulary.Received(1).Save(Arg.Any<Vocabulary>());
    }

    public static TheoryData<string, UpdateVocabularyRequest> OverALimit() => new()
    {
        {
            "hint words",
            new UpdateVocabularyRequest(
                Enumerable.Range(0, UpdateVocabularyRequest.MaxHintWords + 1).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList(), [])
        },
        { "hint word can be", new UpdateVocabularyRequest([new string('w', UpdateVocabularyRequest.MaxHintWordLength + 1)], []) },
        {
            "rules",
            new UpdateVocabularyRequest([], Enumerable.Repeat(new ReplacementRule("a", "b"), UpdateVocabularyRequest.MaxReplacements + 1).ToList())
        },
        { "What you say", new UpdateVocabularyRequest([], [new ReplacementRule(new string('s', UpdateVocabularyRequest.MaxSpokenLength + 1), "")]) },
        { "What a rule writes", new UpdateVocabularyRequest([], [new ReplacementRule("s", new string('w', UpdateVocabularyRequest.MaxWrittenLength + 1))]) },
    };

    /// <summary>A vocabulary past a limit is refused with a reason the page can show, never cut down without a word.</summary>
    [Theory]
    [MemberData(nameof(OverALimit))]
    public async Task UpdateVocabulary_OverALimit_ThrowsAndDoesNotSave(string reason, UpdateVocabularyRequest request)
    {
        var ex = await Should.ThrowAsync<ArgumentException>(
            () => new UpdateVocabularyHandler(_vocabulary).Handle(request, CancellationToken.None));

        ex.Message.ShouldContain(reason);
        _vocabulary.DidNotReceiveWithAnyArgs().Save(default!);
    }

    /// <summary>The limits apply to what is saved: padding and repeats that tidying removes do not count.</summary>
    [Fact]
    public async Task UpdateVocabulary_LimitsCountAfterTidying_Saves()
    {
        var padded = new string(' ', 50) + new string('w', UpdateVocabularyRequest.MaxHintWordLength) + new string(' ', 50);
        var words = Enumerable.Repeat("same", UpdateVocabularyRequest.MaxHintWords + 10).Append(padded).ToList();

        var result = await new UpdateVocabularyHandler(_vocabulary).Handle(new UpdateVocabularyRequest(words, []), CancellationToken.None);

        result.HintWords.Count.ShouldBe(2);
    }
}
