using MediatR;
using Murmur.Core.Models;

namespace Murmur.Core.Features.Engine;

/// <summary>Published whenever the speech engine moves on (downloading, loading, ready, failed), so open views can show it.</summary>
public sealed class EngineStatusChangedNotification(EngineStatus status) : INotification
{
    public EngineStatus Status { get; } = status;
}
