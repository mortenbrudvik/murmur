using MediatR;
using Murmur.Core.Features.App;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the main window that the keyboard hook could not be installed, so it can say the hotkey is off.</summary>
public class InputStatusEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<InputStatusChangedNotification>
{
    public Task Handle(InputStatusChangedNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish("app.inputStatusChanged", notification.Status);
        return Task.CompletedTask;
    }
}
