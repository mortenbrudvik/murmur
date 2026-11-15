using MediatR;
using Murmur.Core.Features.App;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the main window to go to its Settings page.</summary>
public class ShowSettingsEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<ShowSettingsNotification>
{
    public Task Handle(ShowSettingsNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish("app.showSettings", new { });
        return Task.CompletedTask;
    }
}
