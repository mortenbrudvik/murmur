using MediatR;
using Murmur.Core.Features.Setup;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the setup page how loud the microphone is during the microphone test.</summary>
public class MicLevelEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<MicLevelNotification>
{
    public Task Handle(MicLevelNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish("setup.micLevel", new { level = notification.Level });
        return Task.CompletedTask;
    }
}
