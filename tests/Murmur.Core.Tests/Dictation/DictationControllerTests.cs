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
    private const int GenericCtrl = 0x11, GenericAlt = 0x12, VkD = 0x44;
    private const int VkLeftCtrl = 0xA2, VkLeftAlt = 0xA4;
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    /// <summary>How long the default hotkey (Right Ctrl, a lone modifier) is held before recording starts.</summary>
    private static readonly TimeSpan ArmDelay = DictationTimings.Default.ArmDelay;

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

    /// <summary>What <see cref="IKeyStateProbe.IsKeyDown"/> reports for the generic Ctrl and Alt keys (a combo's modifiers).</summary>
    private volatile bool _ctrlDown;
    private volatile bool _altDown;

    private DictationController? _controller;

    public DictationControllerTests()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.Ready, "base"));
        _engine.CanTranscribe.Returns(true);
        _settings.Load().Returns(MurmurSettings.Default);
        _vocabulary.Load().Returns(Vocabulary.Empty);
        _keys.IsKeyDown(RightCtrl).Returns(_ => _hotkeyDown);
        _keys.IsKeyDown(GenericCtrl).Returns(_ => _ctrlDown);
        _keys.IsKeyDown(GenericAlt).Returns(_ => _altDown);

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
        DownPastArmDelay();
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
    public void ShortcutWithTheHotkey_NeverOpensTheMicrophone()
    {
        Down(); // Right Ctrl...
        _time.Advance(ArmDelay - TimeSpan.FromMilliseconds(1));

        Signal(HotkeySignal.OtherInputWhileHeld); // ...+C, just within the arm delay
        Up();
        _time.Advance(ArmDelay);

        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _cues.DidNotReceive().PlayStart();
        _pill.DidNotReceive().ShowListening();
        _pill.ReceivedCalls().ShouldBeEmpty(); // not even a Hide, which would take down a notice the pill was showing
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public void ModifierHotkey_StartsOnlyAfterTheArmDelay()
    {
        Down();
        _time.Advance(ArmDelay - TimeSpan.FromMilliseconds(1));

        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _pill.ReceivedCalls().ShouldBeEmpty();
        _cues.ReceivedCalls().ShouldBeEmpty();
        _matcher.DictationActive.ShouldBeFalse(); // so Esc still reaches the app
        _machine.State.ShouldBe(DictationState.Armed);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        _recorder.Received(1).Start(Arg.Any<string?>());
        _pill.Received(1).ShowListening();
        _cues.Received(1).PlayStart();
        _matcher.DictationActive.ShouldBeTrue();
        _machine.State.ShouldBe(DictationState.HoldRecording);
    }

    [Fact]
    public void ModifierHotkey_TapWithinTheArmDelay_RecordsHandsFree()
    {
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(100));

        Up();

        _recorder.Received(1).Start(Arg.Any<string?>());
        _pill.Received(1).ShowListening();
        _matcher.DictationActive.ShouldBeTrue();
        _machine.State.ShouldBe(DictationState.HandsFreeRecording);

        // The recording's 100 ms tick runs.
        _keys.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _keys.Received(1).IsKeyDown(RightCtrl);
    }

    [Fact]
    public void ModifierHotkey_ReleaseMissedWhileArmed_StartsNothingAndForgetsTheHeldKey()
    {
        _matcher.OnKey(new KeyInput(RightCtrl, IsDown: true, IsInjected: false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
        Down();
        _hotkeyDown = false; // released, but the key-up event never arrived

        _time.Advance(ArmDelay);

        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _pill.ReceivedCalls().ShouldBeEmpty();
        _matcher.IsHotkeyHeld.ShouldBeFalse();
        _machine.State.ShouldBe(DictationState.Idle);
        AssertTimerStopped();
    }

    [Fact]
    public void ArmTickBeforeTheDelayIsUp_StartsAtTheNextPoll()
    {
        // A real timer can fire up to a clock tick (15.6 ms) early by the clock the press is timed with. Stamping the
        // press 5 ms after the timer starts counting has the same effect: the first poll comes 5 ms too soon.
        _hotkeyDown = true;
        Controller.HandleSignal(HotkeySignal.HotkeyDown, _time.GetUtcNow() + TimeSpan.FromMilliseconds(5));

        _time.Advance(ArmDelay);
        _machine.State.ShouldBe(DictationState.Armed);
        _recorder.DidNotReceiveWithAnyArgs().Start(default);

        // The next poll follows within a few milliseconds, not a whole 100 ms tick later.
        _time.Advance(TimeSpan.FromMilliseconds(10));
        _recorder.Received(1).Start(Arg.Any<string?>());
        _machine.State.ShouldBe(DictationState.HoldRecording);
    }

    /// <summary>
    /// The UI thread is busy for the whole press, so the tick that would have started the hold never runs and the release
    /// finds the press still armed. The user talked and let go: starting hands-free now would record after they finished.
    /// </summary>
    [Fact]
    public void ModifierHotkey_ReleasedPastTheTapThresholdWhileStillArmed_StartsNothing()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        ui.Run(Down);
        _time.Advance(TimeSpan.FromMilliseconds(1500)); // the ticks queue up behind the busy UI thread

        _hotkeyDown = false;
        ui.Run(() => Controller.HandleSignal(HotkeySignal.HotkeyUp, _time.GetUtcNow()));
        ui.RunPending(); // the ticks that queued up, now stale

        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _pill.ReceivedCalls().ShouldBeEmpty();
        _cues.ReceivedCalls().ShouldBeEmpty();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped(ui);
    }

    /// <summary>The press reaches the UI thread 100 ms after the hook saw it: the arm delay still runs from the press.</summary>
    [Fact]
    public void ModifierHotkey_PressHandledLate_FirstPollIsTheArmDelayAfterThePress()
    {
        _hotkeyDown = true;
        Controller.HandleSignal(HotkeySignal.HotkeyDown, _time.GetUtcNow() - TimeSpan.FromMilliseconds(100));

        _time.Advance(ArmDelay - TimeSpan.FromMilliseconds(101));
        _machine.State.ShouldBe(DictationState.Armed);
        _recorder.DidNotReceiveWithAnyArgs().Start(default);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        _recorder.Received(1).Start(Arg.Any<string?>());
        _machine.State.ShouldBe(DictationState.HoldRecording);
    }

    /// <summary>The press reaches the UI thread after its arm delay is already up: it is decided at once.</summary>
    [Fact]
    public async Task ModifierHotkey_PressHandledAfterTheArmDelay_IsDecidedAtTheFirstPoll()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        _hotkeyDown = true;
        ui.Run(() => Controller.HandleSignal(HotkeySignal.HotkeyDown, _time.GetUtcNow() - TimeSpan.FromMilliseconds(200)));

        ui.RunPending(); // no time passes: the poll is due at once
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        _recorder.Received(1).Start(Arg.Any<string?>());
        _machine.State.ShouldBe(DictationState.HoldRecording);
    }

    [Fact]
    public void OtherInputWhileHeld_CancelsTheRecording()
    {
        DownPastArmDelay();

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
        DownPastArmDelay(); // a new recording starts while the first transcription is still running
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
        DownPastArmDelay();
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

        DownPastArmDelay();

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

        DownPastArmDelay();
        _recorder.Received(2).Start(Arg.Any<string?>());
    }

    [Fact]
    public void RecorderStart_UsesTheConfiguredMicrophone()
    {
        _settings.Load().Returns(MurmurSettings.Default with { MicrophoneId = "mic-2" });

        DownPastArmDelay();

        _recorder.Received(1).Start("mic-2");
    }

    [Fact]
    public async Task HotkeyDown_OpensTheMicrophoneOffTheUiThread()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();

        DownPastArmDelay(ui); // returns while the microphone is still opening

        mic.WaitUntilOpening();
        mic.StartContext.ShouldNotBeSameAs(ui);
        mic.IsOpening.ShouldBeTrue();
        _pill.Received(1).ShowListening();
        _matcher.DictationActive.ShouldBeTrue(); // so the hook catches Esc and a combo key while it opens
        _machine.State.ShouldBe(DictationState.HoldRecording);

        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        mic.Recordings.ShouldBe(1);
        _cues.Received(1).PlayStart(); // its first audio
    }

    [Fact]
    public async Task EscapeWhileTheMicrophoneOpens_ReleasesItOnceOpen()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();

        ui.Run(() => Signal(HotkeySignal.Escape));

        await AssertReleasedOnceOpenAsync(ui, mic);
    }

    [Fact]
    public async Task ComboKeyWhileTheMicrophoneOpens_ReleasesItOnceOpen()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();

        ui.Run(() => Signal(HotkeySignal.OtherInputWhileHeld));

        await AssertReleasedOnceOpenAsync(ui, mic);
    }

    [Fact]
    public async Task ReleaseWhileTheMicrophoneOpens_StopsAfterItOpens()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();
        RaiseSamples(Speech); // the capture thread can deliver audio before Start has returned
        ui.RunPending();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        ui.RunPending();

        ui.Run(Up);

        _machine.State.ShouldBe(DictationState.Transcribing);
        _recorder.DidNotReceive().Stop(); // the open holds the recorder: Stop would block the UI thread until it finished

        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.CurrentWork, WaitLimit);

        Received.InOrder(() =>
        {
            _recorder.Start(Arg.Any<string?>());
            _recorder.Stop();
            _cues.PlayStop();
        });
        mic.ReleasedWhileOpening.ShouldBeFalse();
        mic.Recordings.ShouldBe(0);
        _recorder.DidNotReceive().Cancel();
        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _history.Received(1).Add("Hello world.", "Notepad", true);
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
    }

    [Fact]
    public async Task EscapeAfterAReleaseWhileTheMicrophoneOpens_ReleasesItOnceOpen()
    {
        using var logs = new LogCapture();
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        ui.RunPending();
        ui.Run(Up); // the stop waits for the open

        ui.Run(() => Signal(HotkeySignal.Escape));
        _recorder.DidNotReceive().Cancel();
        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.CurrentWork, WaitLimit);

        _recorder.Received(1).Cancel();
        _recorder.DidNotReceive().Stop();
        mic.Recordings.ShouldBe(0);
        mic.ReleasedWhileOpening.ShouldBeFalse();
        await _engine.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, Ct);
        _pill.Received(1).Hide();
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        _machine.State.ShouldBe(DictationState.Idle);
        logs.Outcomes.ShouldBe(["cancelled"]);
    }

    [Fact]
    public async Task OpenFails_ShowsMicrophoneUnavailable()
    {
        using var logs = new LogCapture();
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        _chunksOnStart = [];
        SynchronizationContext? contextDuringOpen = ui;
        _recorder.When(r => r.Start(Arg.Any<string?>())).Do(_ =>
        {
            contextDuringOpen = SynchronizationContext.Current;
            throw new AudioDeviceException("No microphone");
        });

        DownPastArmDelay(ui);
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        contextDuringOpen.ShouldNotBeSameAs(ui);
        Received.InOrder(() =>
        {
            _pill.ShowListening();
            _pill.ShowNotice("Microphone unavailable"); // replaces the pill shown before the microphone was opened
        });
        _recorder.DidNotReceive().Cancel();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped(ui);
        logs.Outcomes.ShouldBe(["failed"]);

        ui.Run(Up);
        _recorder.DidNotReceive().Stop();

        DownPastArmDelay(ui);
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);
        _recorder.Received(2).Start(Arg.Any<string?>());
    }

    [Fact]
    public async Task OpenFailsAfterARelease_ShowsMicrophoneUnavailable()
    {
        using var logs = new LogCapture();
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly(failure: new AudioDeviceException("No microphone"));
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        ui.RunPending();
        ui.Run(Up); // the stop waits for the open

        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.CurrentWork, WaitLimit);

        _pill.Received(1).ShowNotice("Microphone unavailable");
        _pill.DidNotReceive().ShowTranscribing();
        _pill.DidNotReceive().Hide();
        _recorder.DidNotReceive().Stop();
        _recorder.DidNotReceive().Cancel();
        _cues.DidNotReceive().PlayStop();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped(ui);
        logs.Outcomes.ShouldBe(["failed"]);
    }

    [Fact]
    public async Task QuickRepress_WhileTheFirstOpenIsStillRunning_StartsOnceAfterItOpens()
    {
        using var logs = new LogCapture();
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();

        ui.Run(() => Signal(HotkeySignal.Escape));
        DownPastArmDelay(ui); // pressed again before the first microphone has finished opening

        _recorder.Received(1).Start(Arg.Any<string?>()); // the recorder takes one recording at a time
        _pill.Received(2).ShowListening();

        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        Received.InOrder(() =>
        {
            _recorder.Start(Arg.Any<string?>());
            _recorder.Cancel();
            _recorder.Start(Arg.Any<string?>());
            _cues.PlayStart(); // on the second microphone's audio: the first one's belongs to the cancelled press
        });
        mic.Recordings.ShouldBe(1);
        mic.ReleasedWhileOpening.ShouldBeFalse();
        logs.Events.ShouldNotContain(e => e.Exception != null);
        _machine.State.ShouldBe(DictationState.HoldRecording);
        _matcher.DictationActive.ShouldBeTrue();

        // The second recording is the live one: releasing the hotkey pastes it.
        _time.Advance(TimeSpan.FromMilliseconds(800));
        ui.RunPending();
        ui.Run(Up);
        await ui.RunUntilCompleteAsync(Controller.CurrentWork, WaitLimit);

        _recorder.Received(1).Stop();
        _recorder.Received(1).Cancel();
        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        logs.Outcomes.ShouldBe(["cancelled", "pasted"]);
    }

    [Fact]
    public async Task FaultedOpen_DoesNotStopTheNextDictation()
    {
        using var logs = new LogCapture();
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        var bug = new InvalidOperationException("A bug in the open's continuation");
        var cancels = 0;
        _recorder.When(r => r.Cancel()).Do(_ =>
        {
            if (Interlocked.Increment(ref cancels) == 1)
                throw bug; // the first open faults as it releases the microphone of the press Esc ended
        });
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();
        ui.Run(() => Signal(HotkeySignal.Escape));
        DownPastArmDelay(ui); // the next open waits for the one that is about to fault

        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        _recorder.Received(2).Start(Arg.Any<string?>());
        mic.Recordings.ShouldBe(1);
        logs.Events.ShouldContain(e => e.Level == LogEventLevel.Error && e.Exception == bug);
        _machine.State.ShouldBe(DictationState.HoldRecording);

        _time.Advance(TimeSpan.FromMilliseconds(800));
        ui.RunPending();
        ui.Run(Up);
        await ui.RunUntilCompleteAsync(Controller.CurrentWork, WaitLimit);

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        logs.Outcomes.ShouldBe(["cancelled", "pasted"]);
    }

    [Fact]
    public async Task DisposeWhileTheMicrophoneOpens_ReleasesItOnceOpen()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();

        ui.Run(Controller.Dispose);
        _recorder.DidNotReceive().Cancel();
        _matcher.DictationActive.ShouldBeFalse();

        // Released from the thread pool: an exiting app stops running the UI thread's queue.
        mic.Open();
        mic.WaitUntilReleased();
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        _recorder.Received(1).Cancel();
        _recorder.DidNotReceive().Stop();
        mic.Recordings.ShouldBe(0);
        mic.ReleasedWhileOpening.ShouldBeFalse();
    }

    [Fact]
    public async Task TapWhileTheMicrophoneOpens_RecordsHandsFreeOnceItOpens()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        var mic = OpenSlowly();
        DownPastArmDelay(ui);
        mic.WaitUntilOpening();
        _time.Advance(TimeSpan.FromMilliseconds(50));

        ui.Run(Up); // 200 ms after the press: a tap

        _machine.State.ShouldBe(DictationState.HandsFreeRecording);
        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);
        _recorder.DidNotReceive().Stop();
        _recorder.DidNotReceive().Cancel();
        mic.Recordings.ShouldBe(1);
        _cues.Received(1).PlayStart();
        _machine.State.ShouldBe(DictationState.HandsFreeRecording);
    }

    [Fact]
    public async Task LateHotkeyUp_IsTimedByTheHookTimestamp()
    {
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        DownPastArmDelay(ui);
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        ui.RunPending(); // the 100 ms tick: still held

        _hotkeyDown = false;
        var releasedAt = _time.GetUtcNow(); // the hook stamps the release 250 ms after the press (a tap)...
        _time.Advance(TimeSpan.FromMilliseconds(400)); // ...but the UI thread is busy for another 400 ms
        ui.Run(() => Controller.HandleSignal(HotkeySignal.HotkeyUp, releasedAt));
        ui.RunPending(); // the ticks that queued up behind it

        _machine.State.ShouldBe(DictationState.HandsFreeRecording);
        _recorder.DidNotReceive().Stop();
    }

    [Fact]
    public void CaptureFailedWhileRecording_ShowsMicNoticeAndReturnsIdle()
    {
        DownPastArmDelay();

        RaiseCaptureFailed();

        _pill.Received(1).ShowNotice("Microphone unavailable");
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();

        Up();
        _recorder.DidNotReceive().Stop();
        _ = _engine.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, Ct);

        DownPastArmDelay();
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

        DownPastArmDelay();

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

        DownPastArmDelay();

        _pill.Received(1).ShowNotice("Loading speech model…");
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
    }

    [Fact]
    public void ShortcutWhileTheEngineIsNotReady_ShowsNothing()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.Error, "base"));
        _engine.CanTranscribe.Returns(false);

        Down(); // Right Ctrl...
        _time.Advance(ArmDelay - TimeSpan.FromMilliseconds(1));
        Signal(HotkeySignal.OtherInputWhileHeld); // ...+C
        Up();
        _time.Advance(ArmDelay);

        _pill.ReceivedCalls().ShouldBeEmpty();
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _cues.ReceivedCalls().ShouldBeEmpty();
        _machine.State.ShouldBe(DictationState.Idle);
        AssertTimerStopped();
    }

    [Fact]
    public void ModifierHotkeyHeldPastArmDelayWhileNotReady_ShowsTheNotice()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.Error, "base"));
        _engine.CanTranscribe.Returns(false);

        Down();
        _time.Advance(ArmDelay - TimeSpan.FromMilliseconds(1));
        _pill.ReceivedCalls().ShouldBeEmpty(); // it could still turn out to be a shortcut

        _time.Advance(TimeSpan.FromMilliseconds(1));

        _pill.Received(1).ShowNotice("Speech engine unavailable — open Murmur");
        _pill.DidNotReceive().ShowListening();
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _cues.ReceivedCalls().ShouldBeEmpty();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();

        Up(); // its release starts nothing either
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _pill.Received(1).ShowNotice(Arg.Any<string>());
    }

    [Fact]
    public void ModifierHotkeyTapWhileNotReady_ShowsTheNotice()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.Downloading, "base"));
        _engine.CanTranscribe.Returns(false);
        Down();

        // The notice describes the engine when the tap is decided, not when the key went down.
        _engine.Status.Returns(new EngineStatus(EngineState.Loading, "base"));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        Up();

        _pill.Received(1).ShowNotice("Loading speech model…");
        _pill.DidNotReceive().ShowListening();
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _cues.ReceivedCalls().ShouldBeEmpty();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
    }

    [Fact]
    public void ModifierHotkey_EngineReadyByTheEndOfTheArmDelay_StartsRecording()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.Loading, "base"));
        _engine.CanTranscribe.Returns(false);
        Down();

        _engine.Status.Returns(new EngineStatus(EngineState.Ready, "base"));
        _engine.CanTranscribe.Returns(true);
        _time.Advance(ArmDelay);

        _recorder.Received(1).Start(Arg.Any<string?>());
        _pill.DidNotReceiveWithAnyArgs().ShowNotice(default!);
        _machine.State.ShouldBe(DictationState.HoldRecording);
    }

    [Fact]
    public void ComboHotkeyWhileNotReady_ShowsTheNoticeAtOnce()
    {
        const int vkLeftCtrl = 0xA2, vkLeftAlt = 0xA4, vkD = 0x44;
        _matcher.Hotkey = new HotkeyDefinition(vkD, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Ctrl+Alt+D");
        _matcher.OnKey(new KeyInput(vkLeftCtrl, true, false));
        _matcher.OnKey(new KeyInput(vkLeftAlt, true, false));
        _matcher.OnKey(new KeyInput(vkD, true, false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
        _engine.Status.Returns(new EngineStatus(EngineState.Loading, "base"));
        _engine.CanTranscribe.Returns(false);

        Signal(HotkeySignal.HotkeyDown);

        // A combo is swallowed, so it cannot be part of another app's shortcut: there is nothing to wait for.
        _pill.Received(1).ShowNotice("Loading speech model…");
        _recorder.DidNotReceiveWithAnyArgs().Start(default);
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();
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

        DownPastArmDelay();
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

        DownPastArmDelay();
        _cues.Received(1).PlayStart();
        RaiseSamples(Speech);
        _cues.Received(2).PlayStart();
    }

    [Fact]
    public void SamplesAvailable_PushesLevelsToThePill()
    {
        // 50 ms is one window of 800 samples, so each chunk is one level: the RMS of the whole window.
        _chunksOnStart = [Sine(milliseconds: 50, amplitude: 1f), Sine(milliseconds: 50, amplitude: 0.01f)];

        DownPastArmDelay();

        _pill.Received(1).PushLevel(Arg.Is<float>(db => Math.Abs(db - -3.01f) < 0.05f));
        _pill.Received(1).PushLevel(Arg.Is<float>(db => Math.Abs(db - -43.01f) < 0.05f));
    }

    [Fact]
    public void SamplesAvailable_PushesOneLevelPer50msOfAudio()
    {
        _chunksOnStart = [];
        DownPastArmDelay();

        for (var i = 0; i < 5; i++)
            RaiseSamples(Sine(milliseconds: 10, amplitude: 0.1f)); // the capture buffers are about this long
        _pill.ReceivedWithAnyArgs(1).PushLevel(default);

        for (var i = 0; i < 5; i++)
            RaiseSamples(Sine(milliseconds: 10, amplitude: 0.1f));
        _pill.ReceivedWithAnyArgs(2).PushLevel(default);
    }

    [Fact]
    public void SamplesAvailable_LessThan50msOfAudio_PushesNothing()
    {
        _chunksOnStart = [];
        DownPastArmDelay();

        RaiseSamples(new float[799]);

        _pill.DidNotReceiveWithAnyArgs().PushLevel(default);
    }

    [Fact]
    public void SamplesAvailable_ChunkLongerThanTheWindow_PushesALevelPerWindow()
    {
        _chunksOnStart = [];
        DownPastArmDelay();

        RaiseSamples(Sine(milliseconds: 130, amplitude: 0.1f)); // two windows and a partial one

        _pill.ReceivedWithAnyArgs(2).PushLevel(default);
    }

    [Fact]
    public void SamplesAvailable_WindowStraddlesTwoChunks_PushesTheRmsOfTheWholeWindow()
    {
        _chunksOnStart = [];
        DownPastArmDelay();

        // Half the window at full scale and half at almost nothing: sqrt(0.5) is -3.01 dBFS.
        RaiseSamples(Enumerable.Repeat(1f, 400).ToArray());
        _pill.DidNotReceiveWithAnyArgs().PushLevel(default);
        RaiseSamples(Enumerable.Repeat(0.001f, 400).ToArray());

        _pill.Received(1).PushLevel(Arg.Is<float>(db => Math.Abs(db - -3.01f) < 0.05f));
    }

    [Fact]
    public void SamplesAvailable_NewRecording_StartsAFreshWindow()
    {
        _chunksOnStart = [];
        DownPastArmDelay();
        RaiseSamples(Sine(milliseconds: 30, amplitude: 0.1f)); // 480 samples: part of a window when the recording ends
        Signal(HotkeySignal.Escape);
        Up();

        DownPastArmDelay();
        RaiseSamples(Sine(milliseconds: 30, amplitude: 0.1f)); // another 480 would complete the old recording's window

        _pill.DidNotReceiveWithAnyArgs().PushLevel(default);
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
        PressCtrlAltD();

        _time.Advance(TimeSpan.FromMilliseconds(800));

        _machine.State.ShouldBe(DictationState.HoldRecording);
        _keys.DidNotReceive().IsKeyDown(VkD);

        _matcher.OnKey(new KeyInput(VkD, false, false)).Signal.ShouldBe(HotkeySignal.HotkeyUp);
        Signal(HotkeySignal.HotkeyUp);
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComboHotkey_MissedRelease_StopsAtTheHoldLimit()
    {
        // The key-up never arrives (an elevated window has focus, say), but Ctrl is still held: only the limit ends it.
        PressCtrlAltD();

        _time.Advance(DictationTimings.Default.HoldLimit);
        await Finished();

        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.Received(1).Add("Hello world.", "", false);
        _pill.Received(1).ShowNotice("Saved to history");
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.IsHotkeyHeld.ShouldBeFalse();
        _matcher.DictationActive.ShouldBeFalse();
        AssertTimerStopped();

        // The next press is a press, not an auto-repeat to swallow.
        _matcher.OnKey(new KeyInput(VkD, true, false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
    }

    [Fact]
    public async Task ComboHotkey_HeldJustShortOfTheHoldLimit_KeepsRecording()
    {
        PressCtrlAltD();

        _time.Advance(DictationTimings.Default.HoldLimit - TimeSpan.FromMilliseconds(100));

        _machine.State.ShouldBe(DictationState.HoldRecording);
        _matcher.IsHotkeyHeld.ShouldBeTrue();
        await _inserter.DidNotReceiveWithAnyArgs().InsertAsync(default!, Ct);
        _history.DidNotReceiveWithAnyArgs().Add(default!, default!, default);
    }

    [Fact]
    public async Task ComboHotkey_ModifiersReleasedWithoutKeyUp_EndsTheHold()
    {
        PressCtrlAltD();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        _machine.State.ShouldBe(DictationState.HoldRecording);

        // The hook saw neither the main key nor Ctrl and Alt come up, but the probe reads Ctrl and Alt, which are never
        // swallowed, as up.
        _ctrlDown = false;
        _altDown = false;
        _matcher.IsHotkeyHeld.ShouldBeTrue();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _history.Received(1).Add("Hello world.", "Notepad", true);
        _machine.State.ShouldBe(DictationState.Idle);
        AssertTimerStopped();

        // The next press of the combo is a press, not an auto-repeat to swallow.
        PressCtrlAltD();
        _recorder.Received(2).Start(Arg.Any<string?>());
        _machine.State.ShouldBe(DictationState.HoldRecording);
    }

    /// <summary>
    /// Ctrl and Alt let go a moment before D: the tick ends the hold, while D is still down and auto-repeating. Its
    /// repeats and its key-up stay swallowed (they would type "ddd" into the app, or for Ctrl+F12 open Save As).
    /// </summary>
    [Fact]
    public async Task ComboHotkey_ModifiersReleasedBeforeTheMainKey_EndsTheHoldAndKeepsSwallowingTheMainKey()
    {
        PressCtrlAltD();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        _matcher.OnKey(new KeyInput(VkLeftCtrl, false, false)).ShouldBe(new MatchResult(HotkeySignal.None, false));
        _matcher.OnKey(new KeyInput(VkLeftAlt, false, false)).ShouldBe(new MatchResult(HotkeySignal.None, false));
        _ctrlDown = false;
        _altDown = false;

        _time.Advance(TimeSpan.FromMilliseconds(100));
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
        _machine.State.ShouldBe(DictationState.Idle);

        // D, still held, repeats: swallowed, and no new press.
        _matcher.OnKey(new KeyInput(VkD, true, false)).ShouldBe(new MatchResult(HotkeySignal.None, true));
        _matcher.OnKey(new KeyInput(VkD, true, false)).ShouldBe(new MatchResult(HotkeySignal.None, true));

        // D comes up: swallowed too, and its release starts nothing.
        _matcher.OnKey(new KeyInput(VkD, false, false)).ShouldBe(new MatchResult(HotkeySignal.HotkeyUp, true));
        Signal(HotkeySignal.HotkeyUp);
        _machine.State.ShouldBe(DictationState.Idle);
        _recorder.Received(1).Start(Arg.Any<string?>());

        // A fresh Ctrl+Alt+D is a new press.
        PressCtrlAltD();
        _recorder.Received(2).Start(Arg.Any<string?>());
    }

    [Fact]
    public async Task ComboHotkey_CtrlReleasedBeforeSpace_SpaceRepeatsStaySwallowed()
    {
        const int vkSpace = 0x20;
        _matcher.Hotkey = new HotkeyDefinition(vkSpace, HotkeyModifiers.Ctrl, "Ctrl+Space");
        _ctrlDown = true;
        _matcher.OnKey(new KeyInput(VkLeftCtrl, true, false));
        _matcher.OnKey(new KeyInput(vkSpace, true, false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
        Signal(HotkeySignal.HotkeyDown);
        _time.Advance(TimeSpan.FromMilliseconds(800));

        _matcher.OnKey(new KeyInput(VkLeftCtrl, false, false));
        _ctrlDown = false;
        _time.Advance(TimeSpan.FromMilliseconds(100));
        await Finished();
        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());

        // Stray spaces in the app otherwise.
        for (var i = 0; i < 3; i++)
            _matcher.OnKey(new KeyInput(vkSpace, true, false)).ShouldBe(new MatchResult(HotkeySignal.None, true));
        _matcher.OnKey(new KeyInput(vkSpace, false, false)).ShouldBe(new MatchResult(HotkeySignal.HotkeyUp, true));
    }

    [Fact]
    public void ComboHotkey_OneOfTwoModifiersReleased_KeepsTheHold()
    {
        PressCtrlAltD();
        _ctrlDown = false; // Alt is still down: the hotkey is still being held

        _time.Advance(TimeSpan.FromSeconds(1));

        _machine.State.ShouldBe(DictationState.HoldRecording);
        _matcher.IsHotkeyHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task FunctionKeyHotkey_WithoutModifiers_HoldsUntilReleased()
    {
        // No modifiers is not "all modifiers up": the matcher's view of the swallowed key is all there is.
        const int vkF12 = 0x7B;
        _matcher.Hotkey = new HotkeyDefinition(vkF12, HotkeyModifiers.None, "F12");
        _matcher.OnKey(new KeyInput(vkF12, true, false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
        Signal(HotkeySignal.HotkeyDown);
        _recorder.Received(1).Start(Arg.Any<string?>());

        _time.Advance(TimeSpan.FromSeconds(1));

        _machine.State.ShouldBe(DictationState.HoldRecording);
        _keys.DidNotReceiveWithAnyArgs().IsKeyDown(default);

        _matcher.OnKey(new KeyInput(vkF12, false, false)).Signal.ShouldBe(HotkeySignal.HotkeyUp);
        Signal(HotkeySignal.HotkeyUp);
        await Finished();

        await _inserter.Received(1).InsertAsync("Hello world.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecorderEventsAndTicks_ArePostedToTheCreatingContext()
    {
        _chunksOnStart = [];
        var ui = new QueueSynchronizationContext();
        _controller = Create(ui);
        DownPastArmDelay(ui);
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        RaiseSamples(Speech); // as the capture thread would
        _pill.DidNotReceiveWithAnyArgs().PushLevel(default);
        _cues.DidNotReceive().PlayStart();

        ui.RunPending();
        _pill.ReceivedWithAnyArgs(6).PushLevel(default); // 300 ms of audio is six 50 ms bars
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

        DownPastArmDelay(ui);
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit); // and the chunk raised as it opened
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
        DownPastArmDelay();

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
    /// Ctrl+Alt+D pressed with Ctrl and Alt physically down. D is swallowed by the hook, so the key-state probe never sees
    /// it; the modifiers it does see. The recording starts at once.
    /// </summary>
    private void PressCtrlAltD()
    {
        var starts = _recorder.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAudioRecorder.Start));
        _matcher.Hotkey = new HotkeyDefinition(VkD, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Ctrl+Alt+D");
        _ctrlDown = true;
        _altDown = true;
        _matcher.OnKey(new KeyInput(VkLeftCtrl, true, false));
        _matcher.OnKey(new KeyInput(VkLeftAlt, true, false));
        _matcher.OnKey(new KeyInput(VkD, true, false)).Signal.ShouldBe(HotkeySignal.HotkeyDown);
        Signal(HotkeySignal.HotkeyDown);
        _recorder.Received(starts + 1).Start(Arg.Any<string?>()); // no arm delay: the combo is swallowed, so it is no app's shortcut
    }

    /// <summary>Hotkey pressed and held through the arm delay: the tick has started the recording.</summary>
    private void DownPastArmDelay()
    {
        Down();
        _time.Advance(ArmDelay);
    }

    /// <summary>
    /// As <see cref="DownPastArmDelay()"/>, with the press and the tick that starts the recording run on <paramref name="ui"/>.
    /// </summary>
    private void DownPastArmDelay(QueueSynchronizationContext ui)
    {
        ui.Run(Down);
        _time.Advance(ArmDelay);
        ui.RunPending();
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

    /// <summary>Hold for 800 ms (recording starts after the arm delay), then release: the controller stops and transcribes.</summary>
    private void HoldAndRelease()
    {
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(800));
        Up();
    }

    /// <summary>Press for 100 ms: a tap, released within the arm delay, which starts hands-free recording.</summary>
    private void Tap()
    {
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(100));
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

    /// <summary>As <see cref="AssertTimerStopped()"/>, running whatever the timer posted to <paramref name="ui"/>.</summary>
    private void AssertTimerStopped(QueueSynchronizationContext ui)
    {
        _keys.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromSeconds(1));
        ui.RunPending();
        _keys.DidNotReceiveWithAnyArgs().IsKeyDown(default);
    }

    /// <summary>
    /// Makes the microphone slow to open, like a Bluetooth headset: the first Start blocks the thread it runs on until
    /// <see cref="SlowMicrophone.Open"/>, then delivers speech (or throws <paramref name="failure"/>).
    /// </summary>
    private SlowMicrophone OpenSlowly(Exception? failure = null)
    {
        _chunksOnStart = []; // the slow microphone raises its own audio once open
        return new SlowMicrophone(_recorder, () => RaiseSamples(Speech), failure);
    }

    /// <summary>
    /// After Esc or a combo key while the microphone opens: the dictation ends at once, and the microphone is cancelled
    /// (not stopped) once its open has finished, never while it runs. The audio it delivered meanwhile is ignored.
    /// </summary>
    private async Task AssertReleasedOnceOpenAsync(QueueSynchronizationContext ui, SlowMicrophone mic)
    {
        _pill.Received(1).Hide();
        _machine.State.ShouldBe(DictationState.Idle);
        _matcher.DictationActive.ShouldBeFalse();
        _recorder.DidNotReceive().Cancel(); // the open holds the recorder: Cancel would block the UI thread until it finished

        mic.Open();
        await ui.RunUntilCompleteAsync(Controller.MicrophoneOpening, WaitLimit);

        _recorder.Received(1).Cancel();
        _recorder.DidNotReceive().Stop();
        mic.Recordings.ShouldBe(0);
        mic.ReleasedWhileOpening.ShouldBeFalse();
        _cues.DidNotReceive().PlayStart();
        _pill.DidNotReceiveWithAnyArgs().PushLevel(default);
        AssertTimerStopped(ui);
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
    /// Sends log events to a sink until disposed. Create the controller after it: the controller takes its logger from
    /// <see cref="Log.Logger"/> when it is created.
    /// </summary>
    private sealed class LogCapture : IDisposable
    {
        private readonly ILogger _previous = Log.Logger;
        private readonly CapturingSink _sink = new();

        public LogCapture() =>
            Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();

        /// <summary>The controller's events.</summary>
        public LogEvent[] Events => _sink.Events;

        /// <summary>The outcome of each dictation's log line, in order.</summary>
        public string[] Outcomes =>
        [
            .. Events
                .Where(e => e.Properties.ContainsKey("Outcome"))
                .Select(e => (string)((ScalarValue)e.Properties["Outcome"]).Value!),
        ];

        public void Dispose() => Log.Logger = _previous;
    }

    /// <summary>
    /// A fake microphone whose first open takes as long as the test wants: that Start blocks the thread it runs on until
    /// <see cref="Open"/>. Like the real recorder it holds one recording at a time (a Start while recording throws), and it
    /// notes a Stop or Cancel that arrives while a Start is still running, which the real one would block until the open
    /// finished.
    /// </summary>
    private sealed class SlowMicrophone
    {
        private readonly ManualResetEventSlim _opening = new();
        private readonly ManualResetEventSlim _open = new();
        private readonly ManualResetEventSlim _released = new();
        private readonly Action _deliverAudio;
        private readonly Exception? _failure;
        private int _starts;
        private int _running;
        private int _recordings;
        private volatile bool _releasedWhileOpening;

        /// <param name="deliverAudio">Raises the microphone's audio once it is open, on the thread that opened it.</param>
        /// <param name="failure">Thrown by the first Start once it is let go, instead of opening.</param>
        public SlowMicrophone(IAudioRecorder recorder, Action deliverAudio, Exception? failure)
        {
            _deliverAudio = deliverAudio;
            _failure = failure;
            recorder.When(r => r.Start(Arg.Any<string?>())).Do(_ => Start());
            recorder.When(r => r.Stop()).Do(_ => End());
            recorder.When(r => r.Cancel()).Do(_ => End());
        }

        /// <summary><see cref="SynchronizationContext.Current"/> inside the first Start.</summary>
        public SynchronizationContext? StartContext { get; private set; }

        /// <summary>A Start is still running.</summary>
        public bool IsOpening => Volatile.Read(ref _running) > 0;

        /// <summary>The recordings it holds: 1 while recording, 0 once stopped or cancelled.</summary>
        public int Recordings => Volatile.Read(ref _recordings);

        /// <summary>A Stop or Cancel arrived while a Start was still running.</summary>
        public bool ReleasedWhileOpening => _releasedWhileOpening;

        public void WaitUntilOpening() => _opening.Wait(WaitLimit).ShouldBeTrue("The microphone never started opening.");

        public void WaitUntilReleased() => _released.Wait(WaitLimit).ShouldBeTrue("The microphone was never released.");

        /// <summary>Lets the first Start finish.</summary>
        public void Open() => _open.Set();

        private void Start()
        {
            Interlocked.Increment(ref _running);
            try
            {
                if (Interlocked.Increment(ref _starts) == 1)
                {
                    StartContext = SynchronizationContext.Current;
                    _opening.Set();
                    _open.Wait(WaitLimit);
                    if (_failure is not null)
                        throw _failure;
                }

                if (Interlocked.Increment(ref _recordings) > 1)
                {
                    Interlocked.Decrement(ref _recordings);
                    throw new InvalidOperationException("A recording is already in progress.");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }

            _deliverAudio();
        }

        private void End()
        {
            if (IsOpening)
                _releasedWhileOpening = true;

            Interlocked.Exchange(ref _recordings, 0);
            _released.Set();
        }
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

        /// <summary>Runs what is queued, then pumps the queue until <paramref name="task"/> completes.</summary>
        public async Task RunUntilCompleteAsync(Task task, TimeSpan limit)
        {
            var stopwatch = Stopwatch.StartNew();
            RunPending();
            while (!task.IsCompleted)
            {
                if (stopwatch.Elapsed > limit)
                    throw new TimeoutException("The controller's work did not finish.");

                await Task.Delay(5, TestContext.Current.CancellationToken);
                RunPending();
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
