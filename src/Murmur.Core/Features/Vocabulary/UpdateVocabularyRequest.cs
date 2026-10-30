using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Vocabulary;

public record UpdateVocabularyRequest(IReadOnlyList<string> HintWords, IReadOnlyList<ReplacementRule> Replacements)
    : IRequest<Murmur.Core.Models.Vocabulary>;

/// <summary>
/// Saves the vocabulary after tidying what the editor sent: hint words are trimmed, blanks dropped and duplicates
/// removed ignoring case (the first spelling wins); rules with a blank spoken text are dropped.
/// </summary>
public class UpdateVocabularyHandler(IVocabularyService vocabularyService)
    : IRequestHandler<UpdateVocabularyRequest, Murmur.Core.Models.Vocabulary>
{
    public Task<Murmur.Core.Models.Vocabulary> Handle(UpdateVocabularyRequest request, CancellationToken ct)
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

        var vocabulary = new Murmur.Core.Models.Vocabulary(hintWords, replacements);
        vocabularyService.Save(vocabulary);
        return Task.FromResult(vocabulary);
    }
}
