using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Audio;
using Murmur.Core.Dictation;
using Murmur.Core.Features.History;
using Murmur.Core.Input;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;

namespace Murmur.Core.Tests.Dictation;

public sealed class DictationControllerTests : IDisposable
{
    private const int RightCtrl = 0xA3;
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    /// <summary>300 ms of a 0.1-amplitude sine at 16 kHz: loud enough for the speech detector.</summary>
    private static readonly float[] Speech = Sine(milliseconds: 300, amplitude: 0.1f);

    private readonly DictationStateMachine _machine = new(DictationTimings.Default);
    private readonly HotkeyMatcher _matcher = new(HotkeyDefinition.Default);
    private readonly IAudioRecorder _recorder = Substitute.For<IAudioRecorder>();
    private readonly ISpeechEngine _engine = Substitute.For<ISpeechEngine>();
    private readonly ITextInserter _inserter = Substitute.For<ITextInserter>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly IAudioArchive _audioArchive = Substitute.For<IAudioArchive>();
    private readonly IPillPresenter _pill = Substitute.For<IPillPresenter>();
    private readonly ISoundCues _cues = Substitute.For<ISoundCues>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IVocabularyService _vocabulary = Substitute.For<IVocabularyService>();
    private readonly IKeyStateProbe _keys = Substitute.For<IKeyStateProbe>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly FakeTimeProvider _time = new();
    private readonly List<HistoryEntry> _added = [];

    /// <summary>Chunks the fake recorder raises from inside <see cref="IAudioRecorder.Start"/>.</summary>
    private float[][] _chunksOnStart = [Speech];

    /// <summary>What <see cref="IKeyStateProbe.IsKeyDown"/> reports for Right Ctrl.</summary>
    private volatile bool _hotkeyDown;

    private DictationController? _controller;

    public DictationControllerTests()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.Ready, "base"));
        _engine.CanTranscribe.Returns(true);
        _settings.Load().Returns(MurmurSettings.Default);
        _vocabulary.Load().Returns(Vocabulary.Empty);
        _keys.IsKeyDown(RightCtrl).Returns(_ => _hotkeyDown);

        _recorder.When(r => r.Start(Arg.Any<string?>())).Do(_ =>
        {
            foreach (var chunk in _chunksOnStart)
                RaiseSamples(chunk);
        });
        _recorder.Stop().Returns(Speech);

        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult("Hello world.", 0.01f));
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InsertResult(InsertOutcome.Pasted, "Notepad"));
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

    private DictationController Controller => _controller ??= Create(context: null);

    public void Dispose() => _controller?.Dispose();

    [Fact]
    public async Task HoldAndRelease_WithSpeech_PastesAndSavesHistory()
    {
        Down();
        _matcher.DictationActive.ShouldBeTrue();
        _pill.Received(1).ShowListening();

        _time.Advance(TimeSpan.FromMilliseconds(800));
        Up();
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _history.Received(1).Add("Hello world.", "Notepad", true);
        await _mediator.Received(1).Publish(
            Arg.Is<HistoryEntryAddedNotification>(n => n.Entry == _added.Single()), Arg.Any<CancellationToken>());
        _pill.Received(1).ShowTranscribing();
        _pill.Received(1).Hide();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public async Task Release_WithoutSpeech_DoesNothing()
    {
        _chunksOnStart = [];

        HoldAndRelease();
        await Finished();

        _recorder.Received(1).Stop();
        await _engine.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, Ct);
        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public async Task Release_WithOnlyQuietAudio_DoesNothing()
    {
        _chunksOnStart = [Sine(milliseconds: 300, amplitude: 0.001f)];

        HoldAndRelease();
        await Finished();

        await _engine.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, Ct);
        _pill.Received(1).Hide();
    }

    [Fact]
    public async Task HighNoSpeechProbability_TreatedAsSilence()
    {
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult("Thank you.", 0.8f));

        HoldAndRelease();
        await Finished();

        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task PunctuationOnlyTranscript_TreatedAsSilence()
    {
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult(" … ", 0f));

        HoldAndRelease();
        await Finished();

        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
        _pill.Received(1).Hide();
    }

    [Fact]
    public async Task VocabularyRulesApplied_BeforeInsert()
    {
        _vocabulary.Load().Returns(new Vocabulary([], [new ReplacementRule("web view two", "WebView2")]));
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult("use web view two", 0.01f));

        HoldAndRelease();
        await Finished();

        await _inserter.Received(1).InsertAsync("use WebView2", Arg.Any<CancellationToken>());
        _history.Received(1).Add("use WebView2", "Notepad", true);
    }

    [Fact]
    public async Task PostProcessors_RunInAscendingOrder()
    {
        var late = Substitute.For<ITranscriptPostProcessor>();
        late.Order.Returns(100);
        late.Process(Arg.Any<string>()).Returns(call => call.Arg<string>() + " late");
        var early = Substitute.For<ITranscriptPostProcessor>();
        early.Order.Returns(0);
        early.Process(Arg.Any<string>()).Returns(call => call.Arg<string>() + " early");
        _controller = Create(context: null, postProcessors: [late, early]);

        HoldAndRelease();
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world. early late", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromptBuiltFromHintWords()
    {
        _vocabulary.Load().Returns(new Vocabulary(["Murmur", "WebView2"], []));

        HoldAndRelease();
        await Finished();

        await _engine.Received(1).TranscribeAsync(Arg.Any<float[]>(), "Murmur, WebView2.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElevatedTarget_ShowsCopiedNotice()
    {
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InsertResult(InsertOutcome.LeftOnClipboard, "Task Manager"));

        HoldAndRelease();
        await Finished();

        _pill.Received(1).ShowNotice("Copied — press Ctrl+V");
        _pill.DidNotReceive().Hide();
        _history.Received(1).Add("Hello world.", "Task Manager", false);
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task HandsFreeTimeLimit_SavesWithoutPasting()
    {
        Tap();
        _machine.State.ShouldBe(DictationState.HandsFreeRecording);

        _time.Advance(TimeSpan.FromMinutes(10));
        await Finished();

        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.Received(1).Add("Hello world.", "", false);
        _pill.Received(1).ShowNotice("Saved to history");
        _pill.DidNotReceive().Hide();
        await _mediator.Received(1).Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>());
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public async Task HandsFree_SecondTap_Pastes()
    {
        Tap();
        _time.Advance(TimeSpan.FromSeconds(3));
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        Up();
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void HandsFreeNoSpeech_AutoCancelsAfter8s()
    {
        _chunksOnStart = [];
        Tap();

        _time.Advance(TimeSpan.FromSeconds(8));

        _recorder.Received(1).Cancel();
        _recorder.DidNotReceive().Stop();
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public void OtherInputWhileHeld_CancelsTheRecording()
    {
        Down();

        Signal(HotkeySignal.OtherInputWhileHeld);

        _recorder.Received(1).Cancel();
        _recorder.DidNotReceive().Stop();
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public async Task EscapeDuringTranscription_DiscardsResult()
    {
        var pending = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transcribeToken = CancellationToken.None;
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            transcribeToken = call.ArgAt<CancellationToken>(2);
            return pending.Task;
        });
        HoldAndRelease();
        _machine.State.ShouldBe(DictationState.Transcribing);
        _matcher.DictationActive.ShouldBeTrue(); // so the hook intercepts Esc

        Signal(HotkeySignal.Escape);

        transcribeToken.IsCancellationRequested.ShouldBeTrue();
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();

        pending.SetResult(new TranscriptionResult("Hello world.", 0.01f));
        await Finished();

        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
        _pill.Received(1).Hide();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
    }

    [Fact]
    public async Task StaleTranscription_FinishingDuringANewRecording_LeavesTheNewRecordingAlone()
    {
        var pending = new TaskCompletionSource<TranscriptionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        HoldAndRelease();
        var first = Controller.CurrentWork;
        Signal(HotkeySignal.Escape);
        Down(); // a new recording starts while the first transcription is still running
        _pill.ClearReceivedCalls();
        _cues.ClearReceivedCalls();

        pending.SetResult(new TranscriptionResult("Hello world.", 0.01f));
        await first.WaitAsync(WaitLimit, Ct);

        _machine.State.ShouldBe(DictationState.HoldRecording);
        _matcher.DictationActive.ShouldBeTrue();
        _pill.ReceivedCalls().ShouldBeEmpty();
        _cues.ReceivedCalls().ShouldBeEmpty();
        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);

        // The new recording's timer still runs.
        _keys.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _keys.Received(1).IsKeyDown(RightCtrl);
    }

    [Fact]
    public async Task EscapeBeforeThePasteIsSent_DiscardsTheDictation()
    {
        var insertStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            // Like the real inserter still waiting for a modifier to be released: cancelling stops it before Ctrl+V.
            var ct = call.ArgAt<CancellationToken>(1);
            var result = new TaskCompletionSource<InsertResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => result.TrySetCanceled(ct));
            insertStarted.SetResult();
            return result.Task;
        });
        HoldAndRelease();
        await insertStarted.Task.WaitAsync(WaitLimit, Ct);

        Signal(HotkeySignal.Escape);
        await Finished();

        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!);
        _pill.Received(1).Hide();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task EscapeAfterThePasteIsSent_StillSavesTheEntry()
    {
        var pending = new TaskCompletionSource<InsertResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var insertStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            insertStarted.SetResult();
            return pending.Task;
        });
        HoldAndRelease();
        await insertStarted.Task.WaitAsync(WaitLimit, Ct);

        Signal(HotkeySignal.Escape);
        pending.SetResult(new InsertResult(InsertOutcome.Pasted, "Notepad")); // Ctrl+V was already sent
        await Finished();

        _history.Received(1).Add("Hello world.", "Notepad", true); // the text is in the target app
        await _mediator.Received(1).Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>());
        _pill.Received(1).Hide(); // by the Esc; nothing touches the pill afterwards
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task Paste_FinishesWithoutWaitingForTheClipboardRestore()
    {
        var clipboard = Substitute.For<IClipboard>();
        clipboard.Capture().Returns(ClipboardSnapshot.Empty);
        var foreground = Substitute.For<IForegroundInfo>();
        foreground.Capture().Returns(new ForegroundTarget(1, "Notepad", false));
        var keyboard = Substitute.For<IKeyboardSender>();
        var inserter = new TextInserter(clipboard, foreground, keyboard, Substitute.For<IKeyStateProbe>(), _time);
        _controller = Create(context: null, inserter: inserter);

        HoldAndRelease();
        await Finished(); // no fake time passes, so this cannot include the 500 ms restore

        keyboard.Received(1).SendCtrlV();
        clipboard.DidNotReceiveWithAnyArgs().Restore(default!);
        _history.Received(1).Add("Hello world.", "Notepad", true);
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();

        // A press straight after the paste starts a new recording.
        Down();
        _machine.State.ShouldBe(DictationState.HoldRecording);
        _recorder.Received(2).Start(Arg.Any<string?>());
        _pill.Received(2).ShowListening();

        _time.Advance(TimeSpan.FromMilliseconds(500));
        await inserter.PendingRestore.WaitAsync(WaitLimit, Ct);
        clipboard.Received(1).Restore(ClipboardSnapshot.Empty);
    }

    [Fact]
    public async Task EscapeWhileTheSavedEntryIsAnnounced_LeavesThePillAlone()
    {
        var publishStarted = PendingPublish(out var publish);
        Tap();
        _time.Advance(TimeSpan.FromMinutes(10)); // the hands-free limit: save without pasting
        await publishStarted.WaitAsync(WaitLimit, Ct);

        Signal(HotkeySignal.Escape);
        publish.SetResult();
        await Finished();

        _history.Received(1).Add("Hello world.", "", false);
        _pill.Received(1).Hide();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task EscapeWhileThePastedEntryIsAnnounced_HidesThePillOnce()
    {
        var publishStarted = PendingPublish(out var publish);
        HoldAndRelease();
        await publishStarted.WaitAsync(WaitLimit, Ct);

        Signal(HotkeySignal.Escape);
        publish.SetResult();
        await Finished();

        _history.Received(1).Add("Hello world.", "Notepad", true);
        _pill.Received(1).Hide();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void RecorderStartFails_ShowsMicNoticeAndReturnsIdle()
    {
        _chunksOnStart = [];
        _recorder.When(r => r.Start(Arg.Any<string?>())).Do(_ => throw new AudioDeviceException("No microphone"));

        Down();

        Received.InOrder(() =>
        {
            _pill.ShowListening();
            _pill.ShowNotice("Microphone unavailable"); // replaces the pill shown before the microphone was opened
        });
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();

        Up();
        _recorder.DidNotReceive().Stop();

        Down();
        _recorder.Received(2).Start(Arg.Any<string?>());
    }

    [Fact]
    public void RecorderStart_UsesTheConfiguredMicrophone()
    {
        _settings.Load().Returns(MurmurSettings.Default with { MicrophoneId = "mic-2" });

        Down();

        _recorder.Received(1).Start("mic-2");
    }

    [Fact]
    public void ThePillShowsBeforeTheMicrophoneIsOpened()
    {
        Down();

        Received.InOrder(() =>
        {
            _pill.ShowListening();
            _recorder.Start(Arg.Any<string?>());
        });
    }

    [Fact]
    public void SlowMicrophone_ATapReleasedWhileItOpensIsStillATap()
    {
        // Opening a Bluetooth headset blocks the UI thread for a second; the hook stamps the release meanwhile.
        _recorder.When(r => r.Start(Arg.Any<string?>())).Do(_ => _time.Advance(TimeSpan.FromSeconds(1)));
        var pressedAt = _time.GetUtcNow();

        _hotkeyDown = true;
        Controller.HandleSignal(HotkeySignal.HotkeyDown, pressedAt);
        _hotkeyDown = false;
        Controller.HandleSignal(HotkeySignal.HotkeyUp, pressedAt + TimeSpan.FromMilliseconds(150));

        _machine.State.ShouldBe(DictationState.HandsFreeRecording);
        _recorder.DidNotReceive().Stop();
    }

    [Fact]
    public void LateHotkeyUp_IsTimedByTheHookTimestamp()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        ui.Run(Down);
        ui.RunPending();
        _time.Advance(TimeSpan.FromMilliseconds(150));
        ui.RunPending(); // the 100 ms tick: still held

        _hotkeyDown = false;
        var releasedAt = _time.GetUtcNow(); // the hook stamps the release (a tap)...
        _time.Advance(TimeSpan.FromMilliseconds(400)); // ...but the UI thread is busy for another 400 ms
        ui.Run(() => Controller.HandleSignal(HotkeySignal.HotkeyUp, releasedAt));
        ui.RunPending(); // the ticks that queued up behind it

        _machine.State.ShouldBe(DictationState.HandsFreeRecording);
        _recorder.DidNotReceive().Stop();
    }

    [Fact]
    public void CaptureFailedWhileRecording_ShowsMicNoticeAndReturnsIdle()
    {
        Down();

        RaiseCaptureFailed();

        _pill.Received(1).ShowNotice("Microphone unavailable");
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();

        Up();
        _recorder.DidNotReceive().Stop();
        _ = _engine.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, Ct);

        Down();
        _recorder.Received(2).Start(Arg.Any<string?>());
    }

    [Fact]
    public void CaptureFailedWhenIdle_IsOnlyLogged()
    {
        _ = Controller;

        RaiseCaptureFailed();

        _pill.ReceivedCalls().ShouldBeEmpty();
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Theory]
    [InlineData(EngineState.Loading, null, null, "Loading speech model…")]
    [InlineData(EngineState.NotStarted, null, null, "Loading speech model…")]
    [InlineData(EngineState.Downloading, 380L, 1000L, "Downloading speech model… 38%")]
    [InlineData(EngineState.Downloading, 0L, 1000L, "Downloading speech model… 0%")]
    [InlineData(EngineState.Downloading, 380L, 0L, "Downloading speech model…")]
    [InlineData(EngineState.Downloading, null, null, "Downloading speech model…")]
    [InlineData(EngineState.Error, null, null, "Speech engine unavailable — open Murmur")]
    public void EngineNotReady_ShowsTheStateNotice(EngineState state, long? bytesReceived, long? totalBytes, string expected)
    {
        var download = bytesReceived is { } received
            ? new DownloadProgress("Whisper large-v3-turbo", received, totalBytes ?? 0, 0, null, 0)
            : null;
        _engine.Status.Returns(new EngineStatus(state, "base", Download: download));
        _engine.CanTranscribe.Returns(false);

        Down();

        _pill.Received(1).ShowNotice(expected);
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
    }

    [Fact]
    public async Task SwitchLoadingWithAModelLoaded_StillDictates()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.Loading, "small", Switching: true));
        _engine.CanTranscribe.Returns(true);

        HoldAndRelease();
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
    }

    [Fact]
    public void ReadyStatusWithoutALoadedModel_DoesNotDictate()
    {
        _engine.CanTranscribe.Returns(false);

        Down();

        _pill.Received(1).ShowNotice("Loading speech model…");
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
    }

    [Fact]
    public async Task EngineThrows_ShowsFailedNotice()
    {
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TranscriptionResult>(new InvalidOperationException("boom")));

        HoldAndRelease();
        await Finished();

        _pill.Received(1).ShowNotice("Transcription failed");
        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public async Task InsertThrows_KeepsTheTextInHistory()
    {
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InsertResult>(new Win32Exception(5, "Clipboard busy")));

        HoldAndRelease();
        await Finished();

        _history.Received(1).Add("Hello world.", "", false);
        _pill.Received(1).ShowNotice("Saved to history");
        await _mediator.Received(1).Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>());
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
    }

    [Fact]
    public async Task HistoryAddThrows_PasteStillCountsAndThePillHides()
    {
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(_ => throw new IOException("locked"));

        HoldAndRelease();
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!); // the text is in the target app already
        _pill.Received(1).Hide();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        await _mediator.DidNotReceiveWithAnyArgs().Publish(default(HistoryEntryAddedNotification)!, Ct);
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
    }

    [Fact]
    public async Task ElevatedTarget_HistoryAddThrows_StillShowsCopied()
    {
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InsertResult(InsertOutcome.LeftOnClipboard, "Task Manager"));
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(_ => throw new IOException("locked"));

        HoldAndRelease();
        await Finished();

        _pill.Received(1).ShowNotice("Copied — press Ctrl+V");
        _inserter.DidNotReceiveWithAnyArgs().TryCopy(default!); // the inserter left it on the clipboard already
    }

    [Theory]
    [InlineData(true, "Copied — press Ctrl+V")]
    [InlineData(false, "Transcription failed")]
    public async Task HandsFreeTimeLimit_HistoryAddThrows_FallsBackToTheClipboard(bool copyWorks, string notice)
    {
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(_ => throw new IOException("locked"));
        _inserter.TryCopy(Arg.Any<string>()).Returns(copyWorks);

        Tap();
        _time.Advance(TimeSpan.FromMinutes(10));
        await Finished();

        _inserter.Received(1).TryCopy("Hello world.");
        _pill.Received(1).ShowNotice(notice);
        _pill.DidNotReceive().ShowNotice("Saved to history");
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Theory]
    [InlineData(true, "Copied — press Ctrl+V")]
    [InlineData(false, "Transcription failed")]
    public async Task InsertThrows_HistoryAddThrows_FallsBackToTheClipboard(bool copyWorks, string notice)
    {
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InsertResult>(new Win32Exception(5, "Clipboard busy")));
        _history.Add(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(_ => throw new IOException("locked"));
        _inserter.TryCopy(Arg.Any<string>()).Returns(copyWorks);

        HoldAndRelease();
        await Finished();

        _inserter.Received(1).TryCopy("Hello world.");
        _pill.Received(1).ShowNotice(notice);
        _pill.DidNotReceive().ShowNotice("Saved to history");
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task TextIsTrimmedAfterTheVocabularyRules()
    {
        _vocabulary.Load().Returns(new Vocabulary([], [new ReplacementRule("um", "")]));
        _engine.TranscribeAsync(Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult("Um hello there.", 0.01f));

        HoldAndRelease();
        await Finished();

        await _inserter.Received(1).InsertAsync("hello there.", Arg.Any<CancellationToken>());
        _history.Received(1).Add("hello there.", "Notepad", true);
    }

    [Fact]
    public async Task EachDictation_LogsOneInformationLine_WithoutTheTranscript()
    {
        var sink = new CapturingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            _controller = Create(context: null); // takes its logger from Log.Logger

            HoldAndRelease();
            await Finished();
            _chunksOnStart = [];
            HoldAndRelease();
            await Finished();
        }
        finally
        {
            Log.Logger = previous;
        }

        var lines = sink.Events
            .Where(e => e.Properties.TryGetValue("Outcome", out _))
            .ToArray();
        lines.Select(e => e.Level).ShouldAllBe(level => level == LogEventLevel.Information);
        lines.Select(e => ((ScalarValue)e.Properties["Outcome"]).Value).ShouldBe(["pasted", "silence"]);
        ((ScalarValue)lines[0].Properties["Characters"]).Value.ShouldBe("Hello world.".Length);
        ((ScalarValue)lines[0].Properties["TargetApp"]).Value.ShouldBe("Notepad");
        ((ScalarValue)lines[0].Properties["RecordingMs"]).Value.ShouldBe(800L);
        ((ScalarValue)lines[0].Properties["ReleaseToPasteMs"]).Value.ShouldNotBeNull();
        ((ScalarValue)lines[1].Properties["SpeechDetected"]).Value.ShouldBe(false);
        sink.Events.ShouldContain(e => e.MessageTemplate.Text.StartsWith("Microphone opened in"));
        sink.Events.ShouldNotContain(e => e.RenderMessage(null).Contains("Hello world"));
    }

    [Fact]
    public async Task KeepAudioOn_SavesAudioWithEntryId()
    {
        _settings.Load().Returns(MurmurSettings.Default with { KeepAudio = true });

        HoldAndRelease();
        await Finished();

        _audioArchive.Received(1).Save(_added.Single().Id, Speech);
    }

    [Fact]
    public async Task KeepAudioOff_DoesNotSave()
    {
        HoldAndRelease();
        await Finished();

        _history.ReceivedWithAnyArgs(1).Add(default!, default!, default);
        _audioArchive.DidNotReceiveWithAnyArgs().Save(default, default!);
    }

    [Fact]
    public async Task SoundCuesOff_NoCues()
    {
        _settings.Load().Returns(MurmurSettings.Default with { SoundCues = false });

        HoldAndRelease();
        await Finished();

        _cues.DidNotReceive().PlayStart();
        _cues.DidNotReceive().PlayStop();
    }

    [Fact]
    public async Task StartCue_PlaysWhenTheMicDeliversItsFirstChunk_OncePerRecording()
    {
        _chunksOnStart = [];

        Down();
        _cues.DidNotReceive().PlayStart(); // the mic is not live yet (a Bluetooth headset takes about a second)

        RaiseSamples(Speech);
        _cues.Received(1).PlayStart();
        RaiseSamples(Speech);
        _cues.Received(1).PlayStart();

        _time.Advance(TimeSpan.FromMilliseconds(800));
        Up();
        Received.InOrder(() =>
        {
            _recorder.Stop();
            _cues.PlayStop();
        });
        await Finished();

        Down();
        _cues.Received(1).PlayStart();
        RaiseSamples(Speech);
        _cues.Received(2).PlayStart();
    }

    [Fact]
    public void SamplesAvailable_PushesLevelsToThePill()
    {
        _chunksOnStart = [Sine(milliseconds: 30, amplitude: 1f), Sine(milliseconds: 30, amplitude: 0.01f)];

        Down();

        _pill.Received(1).PushLevel(Arg.Is<float>(db => Math.Abs(db - -3.01f) < 0.05f));
        _pill.Received(1).PushLevel(Arg.Is<float>(db => Math.Abs(db - -43.01f) < 0.05f));
    }

    [Fact]
    public async Task MissedRelease_TickStopsTheRecordingAndForgetsTheHeldKey()
    {
        _matcher.OnKey(new KeyInput(RightCtrl, IsDown: true, IsInjected: false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        _matcher.IsHotkeyHeld.ShouldBeTrue();
        _machine.State.ShouldBe(DictationState.HoldRecording);

        _hotkeyDown = false; // released, but the key-up event never arrived
        _time.Advance(TimeSpan.FromMilliseconds(100));

        _matcher.IsHotkeyHeld.ShouldBeFalse();
        await Finished();
        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task ComboHotkey_TickUsesTheMatchersHeldState()
    {
        // A swallowed combo key never reaches GetAsyncKeyState, so the probe would always say "up".
        const int vkLeftCtrl = 0xA2, vkLeftAlt = 0xA4, vkD = 0x44;
        _matcher.Hotkey = new HotkeyDefinition(vkD, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Ctrl+Alt+D");
        _matcher.OnKey(new KeyInput(vkLeftCtrl, true, false));
        _matcher.OnKey(new KeyInput(vkLeftAlt, true, false));
        _matcher.OnKey(new KeyInput(vkD, true, false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
        Signal(HotkeySignal.HotkeyDown);

        _time.Advance(TimeSpan.FromMilliseconds(800));

        _machine.State.ShouldBe(DictationState.HoldRecording);
        _keys.DidNotReceive().IsKeyDown(vkD);

        _matcher.OnKey(new KeyInput(vkD, false, false)).Signal.ShouldBe(HotkeySignal.HotkeyUp);
        Signal(HotkeySignal.HotkeyUp);
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RecorderEventsAndTicks_ArePostedToTheCreatingContext()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);

        ui.Run(Down); // the fake recorder raises a chunk from inside Start, as the capture thread would
        _pill.DidNotReceiveWithAnyArgs().PushLevel(default);
        _cues.DidNotReceive().PlayStart();

        ui.RunPending();
        _pill.ReceivedWithAnyArgs(1).PushLevel(default);
        _cues.Received(1).PlayStart();

        _keys.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _keys.DidNotReceiveWithAnyArgs().IsKeyDown(default);
        ui.RunPending();
        _keys.Received(1).IsKeyDown(RightCtrl);

        RaiseCaptureFailed();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        ui.RunPending();
        _pill.Received(1).ShowNotice("Microphone unavailable");
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task Insert_RunsOffTheUiThread()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        SynchronizationContext? contextDuringInsert = ui;
        _inserter.InsertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            contextDuringInsert = SynchronizationContext.Current;
            return new InsertResult(InsertOutcome.Pasted, "Notepad");
        });

        ui.Run(Down);
        ui.RunPending();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        ui.RunPending();
        ui.Run(Up);
        await ui.RunUntilCompleteAsync(Controller.CurrentWork, WaitLimit);

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        contextDuringInsert.ShouldNotBeSameAs(ui);
        _history.Received(1).Add("Hello world.", "Notepad", true);
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public void Dispose_CancelsTheRecordingAndStopsListening()
    {
        Down();

        Controller.Dispose();

        _recorder.Received(1).Cancel();
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
        _pill.ClearReceivedCalls();
        RaiseSamples(Speech);
        _pill.ReceivedCalls().ShouldBeEmpty();
    }

    private DictationController Create(
        SynchronizationContext? context, IEnumerable<ITranscriptPostProcessor>? postProcessors = null,
        ITextInserter? inserter = null)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return new DictationController(
                _machine, _matcher, _recorder, _engine,
                postProcessors ?? [new TranscriptCleaner(), new VocabularyReplacer(_vocabulary)],
                inserter ?? _inserter, _history, _audioArchive, _pill, _cues, _settings, _vocabulary, _keys, _mediator,
                _time);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>A signal delivered at once: the hook's timestamp is the current time.</summary>
    private void Signal(HotkeySignal signal) => Controller.HandleSignal(signal, _time.GetUtcNow());

    /// <summary>Hotkey pressed: the key probe reports it down until <see cref="Up"/>.</summary>
    private void Down()
    {
        _hotkeyDown = true;
        Signal(HotkeySignal.HotkeyDown);
    }

    private void Up()
    {
        _hotkeyDown = false;
        Signal(HotkeySignal.HotkeyUp);
    }

    /// <summary>
    /// Makes announcing a history entry wait for <paramref name="publish"/>; the returned task completes once the
    /// announcement has started.
    /// </summary>
    private Task PendingPublish(out TaskCompletionSource publish)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mediator.Publish(Arg.Any<HistoryEntryAddedNotification>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            started.TrySetResult();
            return pending.Task;
        });
        publish = pending;
        return started.Task;
    }

    /// <summary>Hold for 800 ms, then release: the controller stops and transcribes.</summary>
    private void HoldAndRelease()
    {
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        Up();
    }

    /// <summary>Press for 150 ms: a tap, which starts hands-free recording.</summary>
    private void Tap()
    {
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(150));
        Up();
    }

    private Task Finished() => Controller.CurrentWork.WaitAsync(WaitLimit, Ct);

    private void RaiseSamples(float[] chunk) => _recorder.SamplesAvailable += Raise.Event<Action<float[]>>(chunk);

    private void RaiseCaptureFailed() =>
        _recorder.CaptureFailed += Raise.Event<Action<Exception>>(new AudioDeviceException("The device was unplugged"));

    /// <summary>The 100 ms tick no longer runs: time passes without the hotkey being polled.</summary>
    private void AssertTimerStopped()
    {
        _keys.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromSeconds(1));
        _keys.DidNotReceiveWithAnyArgs().IsKeyDown(default);
    }

    private static float[] Sine(int milliseconds, float amplitude, double frequencyHz = 440)
    {
        var samples = new float[16 * milliseconds];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequencyHz * i / 16_000));
        return samples;
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        /// <summary>The controller's events (the logger is global, so other test classes may log here too).</summary>
        public LogEvent[] Events =>
        [
            .. _events.Where(e => e.Properties.TryGetValue(Constants.SourceContextPropertyName, out var source)
                                  && source.ToString().Contains(nameof(DictationController))),
        ];

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }

    /// <summary>
    /// Stands in for the WPF dispatcher: posted callbacks wait in a queue until the test runs them, and
    /// <see cref="Send"/> is forbidden (a synchronous dispatch from the capture thread can deadlock).
    /// </summary>
    private sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
                _queue.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("The controller must never Send to the UI thread.");

        /// <summary>Runs <paramref name="action"/> as if on the UI thread.</summary>
        public void Run(Action action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                action();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        /// <summary>Runs every queued callback, including ones queued while running.</summary>
        public void RunPending() => Run(() =>
        {
            while (TryDequeue(out var item))
                item.Callback(item.State);
        });

        /// <summary>Pumps the queue until <paramref name="task"/> completes.</summary>
        public async Task RunUntilCompleteAsync(Task task, TimeSpan limit)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (stopwatch.Elapsed > limit)
                    throw new TimeoutException("The controller's work did not finish.");

                RunPending();
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            await task;
        }

        private bool TryDequeue(out (SendOrPostCallback Callback, object? State) item)
        {
            lock (_queue)
                return _queue.TryDequeue(out item);
        }
    }
}
