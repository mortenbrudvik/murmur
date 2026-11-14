using MediatR;
using Murmur.Core.Setup;

namespace Murmur.Core.Features.App;

/// <summary>The web UI says it has drawn its first page (the setup page or the normal window).</summary>
public record UiReadyRequest : IRequest;

public class UiReadyHandler(UiReadiness ui) : IRequestHandler<UiReadyRequest>
{
    public Task Handle(UiReadyRequest request, CancellationToken ct)
    {
        ui.MarkReady();
        return Task.CompletedTask;
    }
}
