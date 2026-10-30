using Murmur.Core.Features.Engine;
using Murmur.Common;
using Murmur.Core.Features.History;
using Murmur.Core.Features.Setup;
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
        var status = new EngineStatus(EngineState.Downloading, "Whisper large-v3-turbo",
            Download: new DownloadProgress("Whisper large-v3-turbo", 250, 1000, 100, 7.5, 0));

        await new EngineStatusEventHandler(_events).Handle(new EngineStatusChangedNotification(status), CancellationToken.None);

        _events.Received(1).Publish("engine.statusChanged", status);
    }

    [Fact]
    public async Task MicLevelEventHandler_PublishesSetupMicLevelWithTheLevel()
    {
        await new MicLevelEventHandler(_events).Handle(new MicLevelNotification(0.5, ended: false), CancellationToken.None);

        // The page reads the level as { level, ended }, so that is the shape that goes over the wire.
        _events.Received(1).Publish("setup.micLevel", Arg.Is<object>(data => JsonUtils.Serialize(data) == "{\"level\":0.5,\"ended\":false}"));
    }

    [Fact]
    public async Task MicLevelEventHandler_TestEnded_SaysSo()
    {
        await new MicLevelEventHandler(_events).Handle(new MicLevelNotification(0, ended: true), CancellationToken.None);

        _events.Received(1).Publish("setup.micLevel", Arg.Is<object>(data => JsonUtils.Serialize(data) == "{\"level\":0,\"ended\":true}"));
    }
}
