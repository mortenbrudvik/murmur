using Mediator;
using Murmur.Core.Features.Engine;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the main window how the speech engine is doing (downloading, loading, ready, failed).</summary>
public class EngineStatusEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<EngineStatusChangedNotification>
{
    public ValueTask Handle(EngineStatusChangedNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish(IpcNames.Events.EngineStatusChanged, notification.Status);
        return ValueTask.CompletedTask;
    }
}
