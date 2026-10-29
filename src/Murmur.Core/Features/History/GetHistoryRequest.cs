using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.History;

/// <summary>Newest dictations first; a non-blank <paramref name="Search"/> keeps only those whose text contains it.</summary>
public record GetHistoryRequest(string? Search = null, int Limit = 200) : IRequest<IReadOnlyList<HistoryEntry>>;

public class GetHistoryHandler(IHistoryStore history) : IRequestHandler<GetHistoryRequest, IReadOnlyList<HistoryEntry>>
{
    public Task<IReadOnlyList<HistoryEntry>> Handle(GetHistoryRequest request, CancellationToken ct)
    {
        return Task.FromResult(history.Query(request.Search, request.Limit));
    }
}
