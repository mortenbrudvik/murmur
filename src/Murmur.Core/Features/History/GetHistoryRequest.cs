using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.History;

/// <summary>
/// A page of dictations, newest first; a non-blank <paramref name="Search"/> keeps only those whose text contains it.
/// <paramref name="Before"/> is the id of the oldest entry the caller already has: the page then starts just below it.
/// </summary>
public record GetHistoryRequest(string? Search = null, int Limit = 200, Guid? Before = null) : IRequest<HistoryPageResult>;

public class GetHistoryHandler(IHistoryStore history) : IRequestHandler<GetHistoryRequest, HistoryPageResult>
{
    public Task<HistoryPageResult> Handle(GetHistoryRequest request, CancellationToken ct)
    {
        return Task.FromResult(history.Query(request.Search, request.Limit, request.Before));
    }
}
