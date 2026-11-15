using Mediator;

namespace Murmur.UI.Messaging;

/// <summary>
/// Publishes a notification to its handlers one after another, awaiting each. Because this method is async, a handler
/// that throws faults the returned task instead of throwing into the caller, as with MediatR: the call sites publish
/// and forget (<c>FireAndForget</c> logs the fault), and App raises the engine's StatusChanged, whose later subscribers
/// must still run. Mediator's built-in publishers are not async and let a synchronous throw escape.
/// </summary>
public sealed class AwaitingNotificationPublisher : INotificationPublisher
{
    public async ValueTask Publish<TNotification>(
        NotificationHandlers<TNotification> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        foreach (var handler in handlers)
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
    }
}
