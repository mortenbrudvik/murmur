using MediatR;
using Murmur.Core.Insertion;
using Murmur.Core.Services;

namespace Murmur.Core.Features.History;

/// <summary>
/// Copies one dictation's text for the user to paste. Through the same clipboard write as a paste, which keeps it out
/// of Win+V clipboard history and the cloud clipboard; the page's own navigator.clipboard would not. Returns whether
/// the text is on the clipboard: false when the entry is gone or the clipboard could not be written.
/// </summary>
public record CopyHistoryEntryRequest(Guid Id) : IRequest<bool>;

public class CopyHistoryEntryHandler(IHistoryStore history, ITextInserter inserter)
    : IRequestHandler<CopyHistoryEntryRequest, bool>
{
    public Task<bool> Handle(CopyHistoryEntryRequest request, CancellationToken ct) =>
        Task.FromResult(history.Find(request.Id) is { } entry && inserter.TryCopy(entry.Text));
}
