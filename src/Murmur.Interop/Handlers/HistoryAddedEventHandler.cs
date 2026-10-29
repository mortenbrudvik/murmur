using MediatR;
using Murmur.Core.Features.History;

namespace Murmur.Interop.Handlers;

/// <summary>Tells the main window about a dictation that was just saved, so the history list shows it at once.</summary>
public class HistoryAddedEventHandler(ApiEventAggregator eventAggregator)
    : INotificationHandler<HistoryEntryAddedNotification>
{
    public Task Handle(HistoryEntryAddedNotification notification, CancellationToken ct)
    {
        eventAggregator.Publish("history.added", notification.Entry);
        return Task.CompletedTask;
    }
}
