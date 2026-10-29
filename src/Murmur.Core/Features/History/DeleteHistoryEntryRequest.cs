using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Services;

namespace Murmur.Core.Features.History;

/// <summary>Removes one dictation and its saved audio. Returns whether the entry existed.</summary>
public record DeleteHistoryEntryRequest(Guid Id) : IRequest<bool>;

public class DeleteHistoryEntryHandler(IHistoryStore history, IAudioArchive audioArchive)
    : IRequestHandler<DeleteHistoryEntryRequest, bool>
{
    public Task<bool> Handle(DeleteHistoryEntryRequest request, CancellationToken ct)
    {
        var deleted = history.Delete(request.Id);

        // Also when the entry is already gone: that clears a recording left behind by an earlier failure.
        audioArchive.Delete(request.Id);
        return Task.FromResult(deleted);
    }
}
