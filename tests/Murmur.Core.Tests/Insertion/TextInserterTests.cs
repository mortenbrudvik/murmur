using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Insertion;

public sealed class TextInserterTests
{
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly FakeClipboard _clipboard = new("orig");
    private readonly IForegroundInfo _foreground = Substitute.For<IForegroundInfo>();
    private readonly IKeyboardSender _keyboard = Substitute.For<IKeyboardSender>();
    private readonly IKeyStateProbe _keys = Substitute.For<IKeyStateProbe>();
    private readonly TimerCountingTimeProvider _time = new();
    private readonly List<string?> _clipboardTextAtPaste = [];
    private readonly TextInserter _inserter;

    public TextInserterTests()
    {
        _foreground.Capture().Returns(new ForegroundTarget(1, "Notepad", false));
        _keyboard.When(k => k.SendCtrlV()).Do(_ =>
        {
            lock (_clipboardTextAtPaste)
                _clipboardTextAtPaste.Add(_clipboard.Text);
        });
        _inserter = new TextInserter(_clipboard, _foreground, _keyboard, _keys, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string?[] ClipboardTextAtPaste
    {
        get
        {
            lock (_clipboardTextAtPaste)
                return [.. _clipboardTextAtPaste];
        }
    }

    [Fact]
    public async Task InsertAsync_PastesAndRestoresClipboardAfter500ms()
    {
        var insert = _inserter.InsertAsync("hello", Ct);
        _time.WaitForTimers(1); // the restore delay has started

        _clipboard.Text.ShouldBe("hello");
        _keyboard.Received(1).SendCtrlV();
        ClipboardTextAtPaste.ShouldBe(["hello"]);

        _time.Advance(RestoreDelay - TimeSpan.FromMilliseconds(1));
        insert.IsCompleted.ShouldBeFalse();
        _clipboard.Text.ShouldBe("hello");

        _time.Advance(TimeSpan.FromMilliseconds(1));
        (await insert.WaitAsync(WaitLimit, Ct)).ShouldBe(new InsertResult(InsertOutcome.Pasted, "Notepad"));
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_ClipboardChangedMeanwhile_DoesNotRestore()
    {
        var insert = _inserter.InsertAsync("hello", Ct);
        _time.WaitForTimers(1);

        _clipboard.CopyFromAnotherApp("newer");
        _time.Advance(RestoreDelay);

        (await insert.WaitAsync(WaitLimit, Ct)).Outcome.ShouldBe(InsertOutcome.Pasted);
        _clipboard.Text.ShouldBe("newer");
    }

    [Fact]
    public async Task InsertAsync_ElevatedTarget_LeavesTextOnClipboard()
    {
        _foreground.Capture().Returns(new ForegroundTarget(1, "Unknown app", true));

        var result = await _inserter.InsertAsync("hello", Ct).WaitAsync(WaitLimit, Ct);

        result.ShouldBe(new InsertResult(InsertOutcome.LeftOnClipboard, "Unknown app"));
        _keyboard.DidNotReceive().SendCtrlV();
        _time.Advance(TimeSpan.FromSeconds(1));
        _clipboard.Text.ShouldBe("hello");
    }

    [Fact]
    public async Task InsertAsync_WaitsForModifiersReleasedBeforeSendingPaste()
    {
        var polls = 0;
        _keys.IsAnyModifierDown().Returns(_ => Interlocked.Increment(ref polls) <= 3);
        var start = _time.GetTimestamp();
        TimeSpan? pastedAfter = null;
        _keyboard.When(k => k.SendCtrlV()).Do(_ => pastedAfter = _time.GetElapsedTime(start));

        var insert = _inserter.InsertAsync("hello", Ct);
        for (var poll = 1; poll <= 3; poll++)
        {
            _time.WaitForTimers(poll); // poll `poll` saw a modifier down and is waiting 10 ms
            _keyboard.DidNotReceive().SendCtrlV();
            _time.Advance(PollInterval);
        }

        _time.WaitForTimers(4); // the paste was sent and the restore delay started
        _keyboard.Received(1).SendCtrlV();
        pastedAfter.ShouldBe(TimeSpan.FromMilliseconds(30));
        ClipboardTextAtPaste.ShouldBe(["hello"]);

        _time.Advance(RestoreDelay);
        (await insert.WaitAsync(WaitLimit, Ct)).Outcome.ShouldBe(InsertOutcome.Pasted);
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_ModifiersStillDownAfterOneSecond_PastesAnyway()
    {
        _keys.IsAnyModifierDown().Returns(true);

        var insert = _inserter.InsertAsync("hello", Ct);
        for (var poll = 1; poll <= 100; poll++)
        {
            _time.WaitForTimers(poll);
            _keyboard.DidNotReceive().SendCtrlV();
            _time.Advance(PollInterval);
        }

        _time.WaitForTimers(101);
        _keyboard.Received(1).SendCtrlV();

        _time.Advance(RestoreDelay);
        (await insert.WaitAsync(WaitLimit, Ct)).Outcome.ShouldBe(InsertOutcome.Pasted);
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_TwoConcurrentInserts_RestoresOriginalClipboard()
    {
        var first = _inserter.InsertAsync("a", Ct);
        var second = _inserter.InsertAsync("b", Ct);

        _time.WaitForTimers(1); // "a" pasted and waiting to restore; "b" queued behind it
        ClipboardTextAtPaste.ShouldBe(["a"]);
        _clipboard.CapturedTexts.ShouldBe(["orig"]);

        _time.Advance(RestoreDelay);
        (await first.WaitAsync(WaitLimit, Ct)).Outcome.ShouldBe(InsertOutcome.Pasted);

        _time.WaitForTimers(2); // "b" pasted and waiting to restore
        ClipboardTextAtPaste.ShouldBe(["a", "b"]);
        _clipboard.CapturedTexts.ShouldBe(["orig", "orig"]); // the second snapshot was taken after the first restore

        _time.Advance(RestoreDelay);
        (await second.WaitAsync(WaitLimit, Ct)).Outcome.ShouldBe(InsertOutcome.Pasted);
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_CancelledWhileWaitingForModifiers_RestoresClipboardAndThrows()
    {
        _keys.IsAnyModifierDown().Returns(true);
        using var cts = new CancellationTokenSource();
        var insert = _inserter.InsertAsync("hello", cts.Token);
        _time.WaitForTimers(1);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(insert.WaitAsync(WaitLimit, Ct));
        _keyboard.DidNotReceive().SendCtrlV();
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_CancelledJustBeforePaste_RestoresClipboardAndThrows()
    {
        using var cts = new CancellationTokenSource();
        _foreground.Capture().Returns(_ =>
        {
            cts.Cancel();
            return new ForegroundTarget(1, "Notepad", false);
        });

        await Should.ThrowAsync<OperationCanceledException>(
            _inserter.InsertAsync("hello", cts.Token).WaitAsync(WaitLimit, Ct));

        _keyboard.DidNotReceive().SendCtrlV();
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_CancelledAfterPaste_StillRestoresClipboard()
    {
        using var cts = new CancellationTokenSource();
        var insert = _inserter.InsertAsync("hello", cts.Token);
        _time.WaitForTimers(1);

        await cts.CancelAsync();
        insert.IsCompleted.ShouldBeFalse();
        _time.Advance(RestoreDelay);

        (await insert.WaitAsync(WaitLimit, Ct)).Outcome.ShouldBe(InsertOutcome.Pasted);
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_CancelledWhileQueued_ThrowsWithoutTouchingClipboard()
    {
        var first = _inserter.InsertAsync("a", Ct);
        using var cts = new CancellationTokenSource();
        var second = _inserter.InsertAsync("b", cts.Token);
        _time.WaitForTimers(1);

        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(second.WaitAsync(WaitLimit, Ct));

        _time.Advance(RestoreDelay);
        (await first.WaitAsync(WaitLimit, Ct)).Outcome.ShouldBe(InsertOutcome.Pasted);
        ClipboardTextAtPaste.ShouldBe(["a"]);
        _clipboard.CapturedTexts.ShouldBe(["orig"]);
        _clipboard.Text.ShouldBe("orig");
    }

    [Fact]
    public async Task InsertAsync_PasteKeystrokeFails_LeavesTextOnClipboard()
    {
        _keyboard.When(k => k.SendCtrlV()).Do(_ => throw new Win32Exception(5));

        var result = await _inserter.InsertAsync("hello", Ct).WaitAsync(WaitLimit, Ct);

        result.ShouldBe(new InsertResult(InsertOutcome.LeftOnClipboard, "Notepad"));
        _time.Advance(TimeSpan.FromSeconds(1));
        _clipboard.Text.ShouldBe("hello");
    }

    [Fact]
    public async Task InsertAsync_RestoreFails_StillReportsPasted()
    {
        _clipboard.RestoreFailure = new Win32Exception(5);
        var insert = _inserter.InsertAsync("hello", Ct);
        _time.WaitForTimers(1);

        _time.Advance(RestoreDelay);

        (await insert.WaitAsync(WaitLimit, Ct)).ShouldBe(new InsertResult(InsertOutcome.Pasted, "Notepad"));
    }

    [Fact]
    public async Task InsertAsync_ClipboardUnavailable_ThrowsWithoutPasting()
    {
        var clipboard = Substitute.For<IClipboard>();
        clipboard.Capture().Returns(_ => throw new Win32Exception(5), _ => ClipboardSnapshot.Empty);
        var inserter = new TextInserter(clipboard, _foreground, _keyboard, _keys, _time);

        await Should.ThrowAsync<Win32Exception>(inserter.InsertAsync("hello", Ct).WaitAsync(WaitLimit, Ct));

        clipboard.DidNotReceive().SetText(Arg.Any<string>());
        _keyboard.DidNotReceive().SendCtrlV();

        // A failed insert releases the gate: the next one goes ahead.
        var next = inserter.InsertAsync("again", Ct);
        _time.WaitForTimers(1);
        _keyboard.Received(1).SendCtrlV();
        _time.Advance(RestoreDelay);
        await next.WaitAsync(WaitLimit, Ct);
    }

    /// <summary>In-memory clipboard; like the real one, every write bumps <see cref="SequenceNumber"/>.</summary>
    private sealed class FakeClipboard(string initialText) : IClipboard
    {
        private const uint UnicodeText = 13;

        private readonly Lock _lock = new();
        private readonly List<string?> _capturedTexts = [];
        private IReadOnlyList<ClipboardFormatData> _formats = [TextFormat(initialText)];
        private uint _sequence = 1;

        public Exception? RestoreFailure { get; set; }

        public uint SequenceNumber
        {
            get
            {
                lock (_lock)
                    return _sequence;
            }
        }

        public string? Text
        {
            get
            {
                lock (_lock)
                    return Decode(_formats);
            }
        }

        /// <summary>The clipboard text at the time of each <see cref="Capture"/>.</summary>
        public string?[] CapturedTexts
        {
            get
            {
                lock (_lock)
                    return [.. _capturedTexts];
            }
        }

        public ClipboardSnapshot Capture()
        {
            lock (_lock)
            {
                _capturedTexts.Add(Decode(_formats));
                return _formats.Count == 0 ? ClipboardSnapshot.Empty : new ClipboardSnapshot([.. _formats]);
            }
        }

        public void Restore(ClipboardSnapshot snapshot)
        {
            if (RestoreFailure is { } failure)
                throw failure;

            Write(snapshot.Formats);
        }

        public void SetText(string text) => Write([TextFormat(text)]);

        public void CopyFromAnotherApp(string text) => Write([TextFormat(text)]);

        private void Write(IReadOnlyList<ClipboardFormatData> formats)
        {
            lock (_lock)
            {
                _formats = [.. formats];
                _sequence++;
            }
        }

        private static ClipboardFormatData TextFormat(string text) =>
            new(UnicodeText, Encoding.Unicode.GetBytes(text + '\0'));

        private static string? Decode(IReadOnlyList<ClipboardFormatData> formats) =>
            formats.FirstOrDefault(f => f.Format == UnicodeText) is { } text
                ? Encoding.Unicode.GetString(text.Data).TrimEnd('\0')
                : null;
    }

    /// <summary>
    /// Counts the timers the code under test starts (each <c>Task.Delay</c> starts one), so a test can wait until
    /// the code is parked on a delay before advancing time. Without that, an <c>Advance</c> that races a continuation
    /// still running on the thread pool would be lost.
    /// </summary>
    private sealed class TimerCountingTimeProvider : FakeTimeProvider
    {
        private int _timersCreated;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _timersCreated);
            return timer;
        }

        public void WaitForTimers(int count) =>
            SpinWait.SpinUntil(() => Volatile.Read(ref _timersCreated) >= count, WaitLimit)
                .ShouldBeTrue($"timer {count} was never started");
    }
}
