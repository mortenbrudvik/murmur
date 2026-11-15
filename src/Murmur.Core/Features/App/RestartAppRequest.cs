using Mediator;
using Murmur.Core.Services;

namespace Murmur.Core.Features.App;

/// <summary>Restarts Murmur, for example after the user installed the CUDA runtime.</summary>
public record RestartAppRequest : IRequest;

public class RestartAppHandler(IAppLifecycle lifecycle) : IRequestHandler<RestartAppRequest>
{
    public ValueTask<Unit> Handle(RestartAppRequest request, CancellationToken ct)
    {
        lifecycle.RequestRestart();
        return Unit.ValueTask;
    }
}
