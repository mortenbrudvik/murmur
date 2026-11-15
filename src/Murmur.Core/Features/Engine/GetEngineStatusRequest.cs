using Mediator;
using Murmur.Core.Models;
using Murmur.Core.Speech;

namespace Murmur.Core.Features.Engine;

public record GetEngineStatusRequest : IRequest<EngineStatus>;

public class GetEngineStatusHandler(ISpeechEngine engine) : IRequestHandler<GetEngineStatusRequest, EngineStatus>
{
    public ValueTask<EngineStatus> Handle(GetEngineStatusRequest request, CancellationToken ct)
    {
        return ValueTask.FromResult(engine.Status);
    }
}
