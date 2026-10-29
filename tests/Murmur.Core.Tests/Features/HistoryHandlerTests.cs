using Murmur.Core.Audio;
using Murmur.Core.Features.History;
using Murmur.Core.Models;
using Murmur.Core.Services;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class HistoryHandlerTests
{
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly IAudioArchive _archive = Substitute.For<IAudioArchive>();

    [Fact]
    public async Task GetHistory_PassesSearchAndLimit()
    {
        var entries = new[] { new HistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "abc def", "notepad.exe", true) };
        _history.Query("abc", 50).Returns(entries);

        var result = await new GetHistoryHandler(_history).Handle(new GetHistoryRequest("abc", 50), CancellationToken.None);

        result.ShouldBe(entries);
        _history.Received(1).Query("abc", 50);
    }

    [Fact]
    public async Task GetHistory_WithoutParameters_ListsTheNewest200()
    {
        await new GetHistoryHandler(_history).Handle(new GetHistoryRequest(), CancellationToken.None);

        _history.Received(1).Query(null, 200);
    }

    [Fact]
    public async Task DeleteHistoryEntry_DeletesEntryAndAudio()
    {
        var id = Guid.NewGuid();
        _history.Delete(id).Returns(true);

        var deleted = await new DeleteHistoryEntryHandler(_history, _archive)
            .Handle(new DeleteHistoryEntryRequest(id), CancellationToken.None);

        deleted.ShouldBeTrue();
        _history.Received(1).Delete(id);
        _archive.Received(1).Delete(id);
    }

    [Fact]
    public async Task DeleteHistoryEntry_UnknownId_ReturnsFalseButStillRemovesAnyLeftoverAudio()
    {
        var id = Guid.NewGuid();
        _history.Delete(id).Returns(false);

        var deleted = await new DeleteHistoryEntryHandler(_history, _archive)
            .Handle(new DeleteHistoryEntryRequest(id), CancellationToken.None);

        deleted.ShouldBeFalse();
        _archive.Received(1).Delete(id);
    }

    [Fact]
    public async Task ClearHistory_ClearsBoth()
    {
        var cleared = await new ClearHistoryHandler(_history, _archive)
            .Handle(new ClearHistoryRequest(), CancellationToken.None);

        cleared.ShouldBeTrue();
        _history.Received(1).Clear();
        _archive.Received(1).Clear();
    }
}
