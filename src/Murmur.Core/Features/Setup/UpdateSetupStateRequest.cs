using Mediator;
using Murmur.Core.Models;
using Murmur.Core.Setup;

namespace Murmur.Core.Features.Setup;

/// <summary>Saves the setup progress. Only the fields that are given change; null leaves a field as it is.</summary>
public record UpdateSetupStateRequest(SetupStep? Step, bool? CudaTipDismissed, bool? Completed) : IRequest<SetupState>;

public class UpdateSetupStateHandler(SetupCoordinator coordinator) : IRequestHandler<UpdateSetupStateRequest, SetupState>
{
    public ValueTask<SetupState> Handle(UpdateSetupStateRequest request, CancellationToken ct) =>
        ValueTask.FromResult(coordinator.Update(request.Step, request.CudaTipDismissed, request.Completed));
}
