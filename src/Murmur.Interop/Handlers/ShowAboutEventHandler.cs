using MediatR;
using Murmur.Core.Features.App;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the main window to open its About dialog.</summary>
public class ShowAboutEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<ShowAboutNotification>
{
    public Task Handle(ShowAboutNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish("app.showAbout", new { });
        return Task.CompletedTask;
    }
}
