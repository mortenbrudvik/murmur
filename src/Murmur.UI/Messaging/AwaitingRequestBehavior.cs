using Mediator;

namespace Murmur.UI.Messaging;

/// <summary>
/// Awaits every request's handler, so a handler that throws before its first await faults the task Send returns instead
/// of throwing out of Send, as with MediatR. Callers await Send inside try/catch either way; this keeps the one shape
/// they and their tests expect. Mediator's generated Send is not async and lets a synchronous throw escape.
/// </summary>
public sealed class AwaitingRequestBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken) =>
        await next(message, cancellationToken).ConfigureAwait(false);
}
