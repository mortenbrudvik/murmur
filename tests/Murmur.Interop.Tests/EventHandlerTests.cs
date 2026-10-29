using Murmur.Core.Features.Engine;
using Murmur.Core.Features.History;
using Murmur.Core.Models;
using Murmur.Interop;
using Murmur.Interop.Handlers;
using Murmur.WebView;
using NSubstitute;

namespace Murmur.Interop.Tests;

public class EventHandlerTests
{
    // A substitute aggregator records the Publish calls instead of posting them to a WebView.
    private readonly ApiEventAggregator _events = Substitute.For<ApiEventAggregator>(Substitute.For<IWebViewInterop>());

    [Fact]
    public async Task HistoryAddedEventHandler_PublishesHistoryAddedWithTheEntry()
    {
        var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "hello there", "notepad.exe", Pasted: true);

        await new HistoryAddedEventHandler(_events).Handle(new HistoryEntryAddedNotification(entry), CancellationToken.None);

        _events.Received(1).Publish("history.added", entry);
    }

    [Fact]
    public async Task EngineStatusEventHandler_PublishesEngineStatusChangedWithTheStatus()
    {
        var status = new EngineStatus(EngineState.Downloading, "ggml-large-v3-turbo", DownloadProgress: 0.25);

        await new EngineStatusEventHandler(_events).Handle(new EngineStatusChangedNotification(status), CancellationToken.None);

        _events.Received(1).Publish("engine.statusChanged", status);
    }
}
