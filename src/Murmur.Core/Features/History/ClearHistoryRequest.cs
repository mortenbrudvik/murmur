using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Services;

namespace Murmur.Core.Features.History;

/// <summary>Removes every dictation and every saved recording.</summary>
public record ClearHistoryRequest : IRequest<bool>;

public class ClearHistoryHandler(IHistoryStore history, IAudioArchive audioArchive) : IRequestHandler<ClearHistoryRequest, bool>
{
    public Task<bool> Handle(ClearHistoryRequest request, CancellationToken ct)
    {
        history.Clear();
        audioArchive.Clear();
        return Task.FromResult(true);
    }
}
