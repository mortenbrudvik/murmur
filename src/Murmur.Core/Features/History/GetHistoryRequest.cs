using Mediator;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.History;

/// <summary>
/// A page of dictations, newest first; a non-blank <paramref name="Search"/> keeps only those whose text contains it.
/// <paramref name="Before"/> is the id of the oldest entry the caller already has: the page then starts just below it.
/// <paramref name="Limit"/> is held to 1 through <see cref="MaxLimit"/>.
/// </summary>
public record GetHistoryRequest(string? Search = null, int Limit = 200, Guid? Before = null) : IRequest<HistoryPageResult>
{
    /// <summary>The most entries one page holds, whatever the caller asks for.</summary>
    public const int MaxLimit = 500;
}

public class GetHistoryHandler(IHistoryStore history) : IRequestHandler<GetHistoryRequest, HistoryPageResult>
{
    public ValueTask<HistoryPageResult> Handle(GetHistoryRequest request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, GetHistoryRequest.MaxLimit);
        return ValueTask.FromResult(history.Query(request.Search, limit, request.Before));
    }
}
