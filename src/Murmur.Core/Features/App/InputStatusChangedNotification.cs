using MediatR;
using Murmur.Core.Input;

namespace Murmur.Core.Features.App;

/// <summary>
/// Published when the keyboard hook turns out not to work, so a window that asked before the second attempt ended
/// (the status is read once on load) shows it too.
/// </summary>
public sealed class InputStatusChangedNotification(InputStatus status) : INotification
{
    public InputStatus Status { get; } = status;
}
