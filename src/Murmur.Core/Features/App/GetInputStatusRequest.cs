using Mediator;
using Murmur.Core.Input;

namespace Murmur.Core.Features.App;

/// <summary>Asks whether the keyboard hook that the hotkey needs is installed.</summary>
public record GetInputStatusRequest : IRequest<InputStatus>;

public class GetInputStatusHandler(InputStatus status) : IRequestHandler<GetInputStatusRequest, InputStatus>
{
    public ValueTask<InputStatus> Handle(GetInputStatusRequest request, CancellationToken ct)
    {
        return ValueTask.FromResult(status);
    }
}
