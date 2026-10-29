using MediatR;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Vocabulary;

// Written as Models.Vocabulary because inside this namespace the simple name Vocabulary means the namespace itself.
public record GetVocabularyRequest : IRequest<Models.Vocabulary>;

public class GetVocabularyHandler(IVocabularyService vocabulary) : IRequestHandler<GetVocabularyRequest, Models.Vocabulary>
{
    public Task<Models.Vocabulary> Handle(GetVocabularyRequest request, CancellationToken ct)
    {
        return Task.FromResult(vocabulary.Load());
    }
}
