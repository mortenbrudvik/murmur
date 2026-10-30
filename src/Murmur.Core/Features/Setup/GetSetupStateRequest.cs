using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Setup;

namespace Murmur.Core.Features.Setup;

/// <summary>Whether the first-run setup is shown and at which step; waits for the first-run decision.</summary>
public record GetSetupStateRequest : IRequest<SetupState>;

public class GetSetupStateHandler(SetupCoordinator coordinator) : IRequestHandler<GetSetupStateRequest, SetupState>
{
    public Task<SetupState> Handle(GetSetupStateRequest request, CancellationToken ct) => coordinator.GetStateAsync();
}
