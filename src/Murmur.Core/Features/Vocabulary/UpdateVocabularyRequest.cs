using Mediator;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Vocabulary;

public record UpdateVocabularyRequest(IReadOnlyList<string> HintWords, IReadOnlyList<ReplacementRule> Replacements)
    : IRequest<Murmur.Core.Models.Vocabulary>
{
    /// <summary>The most hint words the vocabulary holds. Far more than fit in the speech model's prompt.</summary>
    public const int MaxHintWords = 500;

    /// <summary>The longest hint word, in characters, after trimming.</summary>
    public const int MaxHintWordLength = 100;

    /// <summary>The most replacement rules the vocabulary holds.</summary>
    public const int MaxReplacements = 500;

    /// <summary>The longest spoken text of a rule, in characters, after trimming.</summary>
    public const int MaxSpokenLength = 200;

    /// <summary>The longest written text of a rule, in characters: room for a short snippet such as a signature.</summary>
    public const int MaxWrittenLength = 2000;
}

/// <summary>
/// Saves the vocabulary after tidying what the editor sent: hint words are trimmed, blanks dropped and duplicates
/// removed ignoring case (the first spelling wins); rules with a blank spoken text are dropped. A vocabulary that is
/// still past one of the limits on <see cref="UpdateVocabularyRequest"/> is not saved: the request fails with an
/// <see cref="ArgumentException"/> that says which limit, rather than losing words or rules without a word.
/// </summary>
public class UpdateVocabularyHandler(IVocabularyService vocabularyService)
    : IRequestHandler<UpdateVocabularyRequest, Murmur.Core.Models.Vocabulary>
{
    public ValueTask<Murmur.Core.Models.Vocabulary> Handle(UpdateVocabularyRequest request, CancellationToken ct)
    {
        var hintWords = (request.HintWords ?? [])
            .Select(w => w?.Trim() ?? "")
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Spoken is trimmed because the replacer matches it trimmed; Written is inserted exactly as typed.
        var replacements = (request.Replacements ?? [])
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Spoken))
            .Select(r => new ReplacementRule(r.Spoken.Trim(), r.Written ?? ""))
            .ToList();

        Validate(hintWords, replacements);

        var vocabulary = new Murmur.Core.Models.Vocabulary(hintWords, replacements);
        vocabularyService.Save(vocabulary);
        return ValueTask.FromResult(vocabulary);
    }

    // The page shows the message as the reason in "Could not save the vocabulary (...)."
    private static void Validate(List<string> hintWords, List<ReplacementRule> replacements)
    {
        if (hintWords.Count > UpdateVocabularyRequest.MaxHintWords)
            throw new ArgumentException($"There can be at most {UpdateVocabularyRequest.MaxHintWords} hint words.");
        if (hintWords.Any(w => w.Length > UpdateVocabularyRequest.MaxHintWordLength))
            throw new ArgumentException($"A hint word can be at most {UpdateVocabularyRequest.MaxHintWordLength} characters long.");
        if (replacements.Count > UpdateVocabularyRequest.MaxReplacements)
            throw new ArgumentException($"There can be at most {UpdateVocabularyRequest.MaxReplacements} rules.");
        if (replacements.Any(r => r.Spoken.Length > UpdateVocabularyRequest.MaxSpokenLength))
            throw new ArgumentException($"What you say in a rule can be at most {UpdateVocabularyRequest.MaxSpokenLength} characters long.");
        if (replacements.Any(r => r.Written.Length > UpdateVocabularyRequest.MaxWrittenLength))
            throw new ArgumentException($"What a rule writes can be at most {UpdateVocabularyRequest.MaxWrittenLength} characters long.");
    }
}
