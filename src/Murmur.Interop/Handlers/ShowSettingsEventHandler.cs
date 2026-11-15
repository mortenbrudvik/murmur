using Mediator;
using Murmur.Core.Features.App;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the main window to go to its Settings page.</summary>
public class ShowSettingsEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<ShowSettingsNotification>
{
    public ValueTask Handle(ShowSettingsNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish(IpcNames.Events.ShowSettings, new { });
        return ValueTask.CompletedTask;
    }
}
