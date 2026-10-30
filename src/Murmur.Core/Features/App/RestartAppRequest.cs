using MediatR;
using Murmur.Core.Services;

namespace Murmur.Core.Features.App;

/// <summary>Restarts Murmur, for example after the user installed the CUDA runtime.</summary>
public record RestartAppRequest : IRequest;

public class RestartAppHandler(IAppLifecycle lifecycle) : IRequestHandler<RestartAppRequest>
{
    public Task Handle(RestartAppRequest request, CancellationToken ct)
    {
        lifecycle.RequestRestart();
        return Task.CompletedTask;
    }
}
