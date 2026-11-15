using Mediator;
using Murmur.Core.Setup;

namespace Murmur.Core.Features.App;

/// <summary>The web UI says it has drawn its first page (the setup page or the normal window).</summary>
public record UiReadyRequest : IRequest;

public class UiReadyHandler(UiReadiness ui) : IRequestHandler<UiReadyRequest>
{
    public ValueTask<Unit> Handle(UiReadyRequest request, CancellationToken ct)
    {
        ui.MarkReady();
        return Unit.ValueTask;
    }
}
