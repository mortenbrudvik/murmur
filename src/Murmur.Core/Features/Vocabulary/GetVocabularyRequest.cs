using MediatR;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Vocabulary;

// Written in full: inside this namespace the simple name Vocabulary means the namespace itself, and Models the
// Murmur.Core.Features.Models namespace.
public record GetVocabularyRequest : IRequest<Murmur.Core.Models.Vocabulary>;

public class GetVocabularyHandler(IVocabularyService vocabulary) : IRequestHandler<GetVocabularyRequest, Murmur.Core.Models.Vocabulary>
{
    public Task<Murmur.Core.Models.Vocabulary> Handle(GetVocabularyRequest request, CancellationToken ct)
    {
        return Task.FromResult(vocabulary.Load());
    }
}
