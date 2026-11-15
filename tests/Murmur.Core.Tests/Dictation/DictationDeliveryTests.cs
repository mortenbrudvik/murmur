using Mediator;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Audio;
using Murmur.Core.Dictation;
using Murmur.Core.Features.History;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Murmur.Core.Tests.Dictation;

public sealed class DictationDeliveryTests
{
    private const string Text = "Hello world.";
    private static readonly float[] Samples = [0.1f, 0.2f, 0.3f];

    private readonly ISpeechEngine _engine = Substitute.For<ISpeechEngine>();
    private readonly ITextInserter _inserter = Substitute.For<ITextInserter>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly IAudioArchive _audioArchive = Substitute.For<IAudioArchive>();
    private readonly IVocabularyService _vocabulary = Substitute.For<IVocabularyService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly List<HistoryEntry> _added = [];
    private bool _current = true;

    public DictationDeliveryTests()
    {
        _vocabulary.Load().Returns(Vocabulary.Empty);
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult(" Hello world. ", 0.01f));
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InsertResult(InsertOutcome.Pasted, "Notepad"));
        _inserter.TryCopy(Arg.Any<string>()).Returns(true);
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(call =>
        {
            var entry = new HistoryEntry(
                Guid.NewGuid(), _time.GetUtcNow(), call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<bool>(2));
            lock (_added)
                _added.Add(entry);
            return entry;
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TranscribeAsync_WithHintWords_PromptsWithThemAndRunsThePostProcessorsInOrderThenTrims()
    {
        _vocabulary.Load().Returns(new Vocabulary(["Murmur", "WebView2"], []));
        var late = Substitute.For<ITranscriptPostProcessor>();
        late.Order.Returns(100);
        late.Process(Arg.Any<string>()).Returns(call => call.Arg<string>() + " late ");
        var early = Substitute.For<ITranscriptPostProcessor>();
        early.Order.Returns(0);
        early.Process(Arg.Any<string>()).Returns(call => call.Arg<string>() + " early");

        var text = await Create([late, early]).TranscribeAsync(Samples, Ct);

        text.ShouldBe("Hello world.  early late");
        await _engine.Received(1).TranscribeAsync(Samples, "Murmur, WebView2.", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0.59f, "Hello world.")]
    [InlineData(0.6f, "")]
    public async Task TranscribeAsync_NoSpeechProbability_IsEmptyFromTheThreshold(float noSpeech, string expected)
    {
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult(Text, noSpeech));

        var text = await Create().TranscribeAsync(Samples, Ct);

        text.ShouldBe(expected);
    }

    [Fact]
    public async Task PasteAsync_Pasted_SavesTheEntryAndReturnsTheTargetAppAndTime()
    {
        var result = await Create().PasteAsync(Text, Samples, keepAudio: false, IsCurrent, Ct);

        result.ShouldBe(new DeliveryResult(DictationOutcome.Pasted, "Notepad", _time.GetUtcNow()));
        await _inserter.Received(1).InsertAsync(Text, Arg.Any<CancellationToken>());
        _history.Received(1).Add(Text, "Notepad", true);
        await _mediator.Received(1).Publish(
            Arg.Is<HistoryEntryAddedNotification>(n => n.Entry == _added.Single()), Arg.Any<CancellationToken>());
        _audioArchive.DidNotReceiveWithAnyArgs().Save(default, default!);
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!);
    }

    [Fact]
    public async Task PasteAsync_LeftOnClipboard_SavesItAsNotPasted()
    {
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InsertResult(InsertOutcome.LeftOnClipboard, "Task Manager"));

        var result = await Create().PasteAsync(Text, Samples, keepAudio: false, IsCurrent, Ct);

        result.ShouldBe(new DeliveryResult(DictationOutcome.LeftOnClipboard, "Task Manager", _time.GetUtcNow()));
        _history.Received(1).Add(Text, "Task Manager", false);
    }

    [Fact]
    public async Task PasteAsync_KeepAudio_SavesTheAudioWithTheEntryId()
    {
        await Create().PasteAsync(Text, Samples, keepAudio: true, IsCurrent, Ct);

        _audioArchive.Received(1).Save(_added.Single().Id, Samples);
    }

    [Fact]
    public async Task PasteAsync_HistoryFails_StillReportsThePaste()
    {
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Throws(new IOException("locked"));

        var result = await Create().PasteAsync(Text, Samples, keepAudio: true, IsCurrent, Ct);

        result.Outcome.ShouldBe(DictationOutcome.Pasted); // the text is in the target app; the failed save is only logged
        _audioArchive.DidNotReceiveWithAnyArgs().Save(default, default!);
        await _mediator.DidNotReceive().Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>());
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!);
    }

    [Fact]
    public async Task PasteAsync_InsertThrows_KeepsTheTextInHistoryWithoutATargetOrTime()
    {
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("clipboard busy"));

        var result = await Create().PasteAsync(Text, Samples, keepAudio: false, IsCurrent, Ct);

        result.ShouldBe(new DeliveryResult(DictationOutcome.SavedOnly));
        _history.Received(1).Add(Text, "", false);
    }

    [Fact]
    public async Task PasteAsync_InsertThrowsAfterTheDictationWasCancelled_KeepsNothing()
    {
        _current = false;
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("clipboard busy"));

        var result = await Create().PasteAsync(Text, Samples, keepAudio: false, IsCurrent, Ct);

        result.ShouldBe(new DeliveryResult(DictationOutcome.Cancelled));
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!);
    }

    [Fact]
    public async Task PasteAsync_CancelledBeforeCtrlV_ThrowsAndKeepsNothing()
    {
        using var cts = new CancellationTokenSource();
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            cts.Cancel();
            return Task.FromCanceled<InsertResult>(call.Arg<CancellationToken>());
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => Create().PasteAsync(Text, Samples, keepAudio: false, IsCurrent, cts.Token));

        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
    }

    [Fact]
    public async Task PasteAsync_PastedAfterTheDictationWasCancelled_StillSavesTheEntry()
    {
        _current = false; // Esc after Ctrl+V: the text has left Murmur

        var result = await Create().PasteAsync(Text, Samples, keepAudio: false, IsCurrent, Ct);

        result.Outcome.ShouldBe(DictationOutcome.Pasted);
        _history.Received(1).Add(Text, "Notepad", true);
    }

    [Fact]
    public async Task KeepUnpastedAsync_Saved_IsSavedOnlyAndAnnounced()
    {
        var outcome = await Create().KeepUnpastedAsync(Text, Samples, keepAudio: true, IsCurrent);

        outcome.ShouldBe(DictationOutcome.SavedOnly);
        _history.Received(1).Add(Text, "", false);
        _audioArchive.Received(1).Save(_added.Single().Id, Samples);
        await _mediator.Received(1).Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>());
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!);
    }

    [Fact]
    public async Task KeepUnpastedAsync_AudioAndAnnouncementFail_StillSavedOnly()
    {
        _audioArchive.When(a => a.Save(Arg.Any<Guid>(), Arg.Any<float[]>())).Do(_ => throw new IOException("disk full"));
        _mediator.Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("no window")));

        var outcome = await Create().KeepUnpastedAsync(Text, Samples, keepAudio: true, IsCurrent);

        outcome.ShouldBe(DictationOutcome.SavedOnly);
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!);
    }

    [Theory]
    [InlineData(true, DictationOutcome.LeftOnClipboard)]
    [InlineData(false, DictationOutcome.Failed)]
    public async Task KeepUnpastedAsync_SaveFails_FallsBackToTheClipboard(bool copied, DictationOutcome expected)
    {
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Throws(new IOException("locked"));
        _inserter.TryCopy(Arg.Any<string>()).Returns(copied);

        var outcome = await Create().KeepUnpastedAsync(Text, Samples, keepAudio: false, IsCurrent);

        outcome.ShouldBe(expected);
        _inserter.Received(1).TryCopy(Text);
        await _mediator.DidNotReceive().Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepUnpastedAsync_SaveFailsAfterTheDictationWasCancelled_DoesNotCopy()
    {
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Throws(new IOException("locked"));
        _current = false;

        var outcome = await Create().KeepUnpastedAsync(Text, Samples, keepAudio: false, IsCurrent);

        outcome.ShouldBe(DictationOutcome.Cancelled);
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!);
    }

    [Fact]
    public async Task KeepUnpastedAsync_WhileAnEarlierSaveIsStillWriting_SavesAfterIt()
    {
        // A dictation cancelled with Esc after Ctrl+V still saves, so its write can still be going when the next one's starts.
        var history = new FirstAddWaitsStore();
        var delivery = new DictationDelivery(
            _engine, [], _inserter, history, _audioArchive, _vocabulary, _mediator, _time);

        var first = delivery.KeepUnpastedAsync("first", Samples, keepAudio: false, IsCurrent);
        await history.FirstWriting.WaitAsync(Ct);
        var second = delivery.KeepUnpastedAsync("second", Samples, keepAudio: false, IsCurrent);

        // Without the wait, the second write gets here and finishes first. With it, this times out.
        await Task.WhenAny(history.SecondWriting, Task.Delay(TimeSpan.FromMilliseconds(200), Ct));
        history.ReleaseFirst();
        (await Task.WhenAll(first, second)).ShouldAllBe(outcome => outcome == DictationOutcome.SavedOnly);

        history.Added.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task KeepUnpastedAsync_AfterAnEarlierSaveFailed_StillSaves()
    {
        _history.Add("first", Arg.Any<string>(), Arg.Any<bool>()).Throws(new IOException("locked"));
        var delivery = Create();

        await delivery.KeepUnpastedAsync("first", Samples, keepAudio: false, IsCurrent);
        var outcome = await delivery.KeepUnpastedAsync("second", Samples, keepAudio: false, IsCurrent);

        outcome.ShouldBe(DictationOutcome.SavedOnly);
        _added.Select(e => e.Text).ShouldBe(["second"]);
    }

    private bool IsCurrent() => _current;

    private DictationDelivery Create(IEnumerable<ITranscriptPostProcessor>? postProcessors = null) =>
        new(_engine, postProcessors ?? [], _inserter, _history, _audioArchive, _vocabulary, _mediator, _time);

    /// <summary>
    /// A history whose Add of "first" waits until <see cref="ReleaseFirst"/>, and which records the order the entries went
    /// in. Hand-written rather than a substitute, which could itself make concurrent calls wait for each other.
    /// </summary>
    private sealed class FirstAddWaitsStore : IHistoryStore
    {
        private readonly TaskCompletionSource _firstWriting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondWriting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _releaseFirst = new();
        private readonly List<string> _added = [];

        public Task FirstWriting => _firstWriting.Task;

        public Task SecondWriting => _secondWriting.Task;

        public string[] Added
        {
            get
            {
                lock (_added)
                    return [.. _added];
            }
        }

        public void ReleaseFirst() => _releaseFirst.Set();

        public HistoryEntry Add(string text, string targetApp, bool pasted)
        {
            if (text == "first")
            {
                _firstWriting.SetResult();
                _releaseFirst.Wait(TimeSpan.FromSeconds(10));
            }
            else
            {
                _secondWriting.SetResult();
            }

            lock (_added)
                _added.Add(text);
            return new HistoryEntry(Guid.NewGuid(), DateTimeOffset.UnixEpoch, text, targetApp, pasted);
        }

        public HistoryPageResult Query(string? search, int limit, Guid? before) => throw new NotSupportedException();

        public HistoryEntry? Find(Guid id) => throw new NotSupportedException();

        public bool Delete(Guid id) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public HistoryTotals Totals() => throw new NotSupportedException();
    }
}
