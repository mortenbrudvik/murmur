using MediatR;
using Murmur.Core.Services;

namespace Murmur.Core.Features.App;

/// <summary>
/// Quits Murmur from the window. The tray menu has Quit too, but it is out of reach for someone who uses only the
/// keyboard and does not know the notification area's keys.
/// </summary>
public record QuitAppRequest : IRequest;

public class QuitAppHandler(IAppLifecycle lifecycle) : IRequestHandler<QuitAppRequest>
{
    public Task Handle(QuitAppRequest request, CancellationToken ct)
    {
        lifecycle.RequestQuit();
        return Task.CompletedTask;
    }
}
