using MediatR;
using Murmur.Core.Models;

namespace Murmur.Core.Features.History;

/// <summary>Published after a dictation was saved to history, so open views can show it.</summary>
public sealed class HistoryEntryAddedNotification(HistoryEntry entry) : INotification
{
    public HistoryEntry Entry { get; } = entry;
}
