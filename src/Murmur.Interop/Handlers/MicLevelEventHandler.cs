using Mediator;
using Murmur.Core.Features.Setup;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the setup page how loud the microphone is during the microphone test, and when the test ended without the page asking.</summary>
public class MicLevelEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<MicLevelNotification>
{
    public ValueTask Handle(MicLevelNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish(IpcNames.Events.MicLevel, new { level = notification.Level, ended = notification.Ended });
        return ValueTask.CompletedTask;
    }
}
