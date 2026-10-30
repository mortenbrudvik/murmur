using System.Runtime.InteropServices;
using MediatR;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Audio;
using Murmur.Core.Features.Setup;
using Murmur.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

public sealed class MicrophoneLevelMonitorTests
{
    private const int AccessDenied = unchecked((int)0x80070005);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly FakeTimeProvider _time = new();
    private readonly List<IAudioRecorder> _created = [];
    private readonly Queue<IAudioRecorder> _prepared = new();
    private readonly List<double> _levels = [];
    private readonly MicrophoneLevelMonitor _monitor;

    public MicrophoneLevelMonitorTests()
    {
        _mediator.Publish(Arg.Do<MicLevelNotification>(n => _levels.Add(n.Level)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _monitor = new MicrophoneLevelMonitor(CreateRecorder, _mediator, _time);
    }

    /// <summary>Each call of the monitor's factory yields the next recorder: a prepared one, else a plain substitute.</summary>
    private IAudioRecorder CreateRecorder()
    {
        var recorder = _prepared.Count > 0 ? _prepared.Dequeue() : Substitute.For<IAudioRecorder>();
        _created.Add(recorder);
        return recorder;
    }

    private IAudioRecorder Recorder(int index = 0) => _created[index];

    /// <summary>A chunk whose RMS is <paramref name="amplitude"/> (a constant signal), which is 20·log10(amplitude) dBFS.</summary>
    private static float[] Chunk(float amplitude) => Enumerable.Repeat(amplitude, 480).ToArray();

    private static void Emit(IAudioRecorder recorder, float[] chunk) =>
        recorder.SamplesAvailable += Raise.Event<Action<float[]>>(chunk);

    [Theory]
    [InlineData(-90, 0)]
    [InlineData(-60, 0)]
    [InlineData(-30, 0.5)]
    [InlineData(0, 1)]
    [InlineData(6, 1)]
    public void LevelFromDbfs_MapsLinearly(double dbfs, double level)
    {
        MicrophoneLevelMonitor.LevelFromDbfs(dbfs).ShouldBe(level, 1e-9);
    }

    [Fact]
    public void MaxDuration_Is120Seconds()
    {
        MicrophoneLevelMonitor.MaxDuration.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Start_DeviceOpens_ReturnsListeningAndPublishesLevels()
    {
        var result = _monitor.Start("mic-1");

        result.ShouldBe(new MicTestResult(MicTestState.Listening));
        Recorder().Received(1).Start("mic-1");

        // 0.03 is -30.46 dBFS, which is a level of 0.492.
        Emit(Recorder(), Chunk(0.03f));

        _levels.Count.ShouldBe(1);
        _levels[0].ShouldBe(MicrophoneLevelMonitor.LevelFromDbfs(20 * Math.Log10(0.03)), 1e-3);
        _levels[0].ShouldBeInRange(0.48, 0.5);
    }

    [Fact]
    public void Start_NoDeviceId_OpensTheDefaultMicrophone()
    {
        _monitor.Start(null).State.ShouldBe(MicTestState.Listening);

        Recorder().Received(1).Start(null);
    }

    [Fact]
    public void Start_SilentSamples_PublishesZero()
    {
        _monitor.Start(null);

        Emit(Recorder(), new float[480]);

        _levels.ShouldBe([0.0]);
    }

    [Fact]
    public void Start_SamplesFasterThan50Ms_PublishesAtMostOncePer50Ms()
    {
        _monitor.Start(null);

        Emit(Recorder(), Chunk(0.1f));
        Emit(Recorder(), Chunk(0.1f));
        _levels.Count.ShouldBe(1);

        _time.Advance(TimeSpan.FromMilliseconds(49));
        Emit(Recorder(), Chunk(0.1f));
        _levels.Count.ShouldBe(1);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Emit(Recorder(), Chunk(0.1f));
        _levels.Count.ShouldBe(2);
    }

    [Fact]
    public void Start_PublishNeverCompletes_DoesNotBlockTheCaptureThread()
    {
        _mediator.Publish(Arg.Any<MicLevelNotification>(), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource().Task);
        _monitor.Start(null);

        Should.CompleteIn(() => Emit(Recorder(), Chunk(0.1f)), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Start_PublishFaults_DoesNotBreakTheCapture()
    {
        _mediator.Publish(Arg.Any<MicLevelNotification>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("no handler"));
        _monitor.Start(null);

        Should.NotThrow(() => Emit(Recorder(), Chunk(0.1f)));
    }

    [Fact]
    public void Start_AccessDenied_ReturnsAccessDenied()
    {
        CreateRecorderThatFails(new AudioDeviceException("Could not start recording", new UnauthorizedAccessException()));

        var result = _monitor.Start(null);

        result.State.ShouldBe(MicTestState.AccessDenied);
    }

    [Fact]
    public void Start_OtherDeviceError_ReturnsFailedWithTheMessage()
    {
        CreateRecorderThatFails(new AudioDeviceException("The microphone is unplugged."));

        var result = _monitor.Start("gone");

        result.ShouldBe(new MicTestResult(MicTestState.Failed, "The microphone is unplugged."));
    }

    [Fact]
    public void Start_DeviceFails_LeavesNothingRunning()
    {
        CreateRecorderThatFails(new AudioDeviceException("The microphone is unplugged."));

        _monitor.Start(null);
        _time.Advance(MicrophoneLevelMonitor.MaxDuration * 2);

        // No timer was left behind: the failed start released its recorder once and nothing else happened.
        Recorder().Received(1).Cancel();
        _levels.ShouldBeEmpty();
    }

    [Fact]
    public void Start_RecorderFactoryThrows_ReturnsFailed()
    {
        var monitor = new MicrophoneLevelMonitor(
            () => throw new InvalidOperationException("no audio stack"), _mediator, _time);

        monitor.Start(null).ShouldBe(new MicTestResult(MicTestState.Failed, "no audio stack"));
    }

    [Fact]
    public void Start_WhileRunning_StopsTheFirstRecorder()
    {
        _monitor.Start("mic-1");
        _monitor.Start("mic-2").State.ShouldBe(MicTestState.Listening);

        Recorder(0).Received(1).Cancel();
        Recorder(1).DidNotReceive().Cancel();
        Recorder(1).Received(1).Start("mic-2");
    }

    [Fact]
    public void Start_WhileRunning_TheFirstRecordersSamplesAreIgnored()
    {
        _monitor.Start("mic-1");
        _monitor.Start("mic-2");

        Emit(Recorder(0), Chunk(0.5f));
        _levels.ShouldBeEmpty();

        Emit(Recorder(1), Chunk(0.5f));
        _levels.Count.ShouldBe(1);
    }

    [Fact]
    public void Start_WhileRunning_RestartsTheAutoStopClock()
    {
        _monitor.Start("mic-1");
        _time.Advance(TimeSpan.FromSeconds(100));
        _monitor.Start("mic-2");

        _time.Advance(TimeSpan.FromSeconds(100));

        Recorder(1).DidNotReceive().Cancel();
        _time.Advance(TimeSpan.FromSeconds(20));
        Recorder(1).Received(1).Cancel();
    }

    [Fact]
    public void Start_After120Seconds_StopsByItself()
    {
        _monitor.Start(null);

        _time.Advance(TimeSpan.FromSeconds(119));
        Recorder().DidNotReceive().Cancel();

        _time.Advance(TimeSpan.FromSeconds(1));
        Recorder().Received(1).Cancel();
    }

    [Fact]
    public void Start_After120Seconds_ShowsASilentMeterAndPublishesNothingMore()
    {
        _monitor.Start(null);
        Emit(Recorder(), Chunk(0.5f));
        _levels.Count.ShouldBe(1);

        _time.Advance(MicrophoneLevelMonitor.MaxDuration);

        // The meter does not freeze at its last level.
        _levels.Count.ShouldBe(2);
        _levels[1].ShouldBe(0);

        _time.Advance(TimeSpan.FromMinutes(5));
        Emit(Recorder(), Chunk(0.5f));
        _levels.Count.ShouldBe(2);
    }

    [Fact]
    public void Start_AfterAutoStop_CanStartAgain()
    {
        _monitor.Start(null);
        _time.Advance(MicrophoneLevelMonitor.MaxDuration);

        _monitor.Start(null).State.ShouldBe(MicTestState.Listening);

        Recorder(1).Received(1).Start(null);
        Recorder(0).Received(1).Cancel();
    }

    [Fact]
    public void Stop_ReleasesTheRecorder()
    {
        _monitor.Start(null);

        _monitor.Stop();

        Recorder().Received(1).Cancel();
    }

    [Fact]
    public void Stop_Twice_IsHarmless()
    {
        _monitor.Start(null);

        _monitor.Stop();
        _monitor.Stop();

        Recorder().Received(1).Cancel();
    }

    [Fact]
    public void Stop_NothingRunning_IsHarmless()
    {
        Should.NotThrow(_monitor.Stop);

        _created.ShouldBeEmpty();
    }

    [Fact]
    public void Stop_CancelsTheAutoStop()
    {
        _monitor.Start(null);
        _monitor.Stop();

        _time.Advance(MicrophoneLevelMonitor.MaxDuration * 2);

        Recorder().Received(1).Cancel();
        _levels.ShouldBeEmpty();
    }

    [Fact]
    public void Stop_LateSamplesPublishNothing()
    {
        _monitor.Start(null);
        _monitor.Stop();

        Emit(Recorder(), Chunk(0.5f));

        _levels.ShouldBeEmpty();
    }

    [Fact]
    public void Stop_RecorderThrows_IsContained()
    {
        _monitor.Start(null);
        Recorder().When(r => r.Cancel()).Throw(new InvalidOperationException("device gone"));

        Should.NotThrow(_monitor.Stop);
    }

    [Fact]
    public void Stop_WhileTheDeviceIsOpening_ReleasesItOnceOpened()
    {
        // The window hid between Start installing the test and the device finishing opening.
        var recorder = Substitute.For<IAudioRecorder>();
        var monitor = new MicrophoneLevelMonitor(() => recorder, _mediator, _time);
        recorder.When(r => r.Start(Arg.Any<string?>())).Do(_ => monitor.Stop());

        var result = monitor.Start(null);

        result.State.ShouldBe(MicTestState.Failed);
        // Once by the Stop that came first, and once more after the late open, so the microphone is not left on.
        recorder.Received(2).Cancel();
        _time.Advance(MicrophoneLevelMonitor.MaxDuration * 2);
        recorder.Received(2).Cancel();
    }

    [Fact]
    public void CaptureFailed_ReleasesTheTestAndShowsASilentMeter()
    {
        _monitor.Start(null);
        Emit(Recorder(), Chunk(0.5f));

        Recorder().CaptureFailed += Raise.Event<Action<Exception>>(new InvalidOperationException("unplugged"));

        _levels.Count.ShouldBe(2);
        _levels[1].ShouldBe(0);

        // The timer is gone, and the test can start again.
        _time.Advance(MicrophoneLevelMonitor.MaxDuration * 2);
        Recorder().Received(1).Cancel();
        _monitor.Start(null).State.ShouldBe(MicTestState.Listening);
        Recorder(1).Received(1).Start(null);
    }

    [Fact]
    public void Dispose_WhileRunning_StopsTheRecorder()
    {
        _monitor.Start(null);

        _monitor.Dispose();

        Recorder().Received(1).Cancel();
        _time.Advance(MicrophoneLevelMonitor.MaxDuration * 2);
        Recorder().Received(1).Cancel();
    }

    [Fact]
    public void Dispose_Twice_IsHarmless()
    {
        _monitor.Start(null);

        _monitor.Dispose();
        _monitor.Dispose();

        Recorder().Received(1).Cancel();
    }

    [Fact]
    public void Start_AfterDispose_DoesNotOpenTheMicrophone()
    {
        _monitor.Dispose();

        var result = _monitor.Start(null);

        result.State.ShouldBe(MicTestState.Failed);
        _created.ForEach(r => r.DidNotReceive().Start(Arg.Any<string?>()));
    }

    [Fact]
    public void Dispose_WhileTheDeviceIsOpening_ReleasesItOnceOpened()
    {
        var recorder = Substitute.For<IAudioRecorder>();
        var monitor = new MicrophoneLevelMonitor(() => recorder, _mediator, _time);
        recorder.When(r => r.Start(Arg.Any<string?>())).Do(_ => monitor.Dispose());

        monitor.Start(null).State.ShouldBe(MicTestState.Failed);

        recorder.Received(2).Cancel();
    }

    [Fact]
    public void Classify_UnauthorizedAccess_IsAccessDenied()
    {
        MicrophoneLevelMonitor.Classify(new UnauthorizedAccessException()).ShouldBe(MicTestState.AccessDenied);
    }

    [Fact]
    public void Classify_UnauthorizedAccessInTheInnerChain_IsAccessDenied()
    {
        var chain = new AudioDeviceException("outer", new InvalidOperationException("middle", new UnauthorizedAccessException()));

        MicrophoneLevelMonitor.Classify(chain).ShouldBe(MicTestState.AccessDenied);
    }

    [Fact]
    public void Classify_AccessDeniedHResult_IsAccessDenied()
    {
        // WASAPI reports a blocked microphone as a COMException with E_ACCESSDENIED.
        var denied = new COMException("Access is denied", AccessDenied);

        MicrophoneLevelMonitor.Classify(new AudioDeviceException("outer", denied)).ShouldBe(MicTestState.AccessDenied);
        MicrophoneLevelMonitor.Classify(denied).ShouldBe(MicTestState.AccessDenied);
    }

    [Fact]
    public void Classify_AccessDeniedInsideAnAggregate_IsAccessDenied()
    {
        var aggregate = new AggregateException(new InvalidOperationException(), new UnauthorizedAccessException());

        MicrophoneLevelMonitor.Classify(aggregate).ShouldBe(MicTestState.AccessDenied);
    }

    [Fact]
    public void Classify_AnythingElse_IsFailed()
    {
        MicrophoneLevelMonitor.Classify(new AudioDeviceException("unplugged")).ShouldBe(MicTestState.Failed);
        MicrophoneLevelMonitor.Classify(new COMException("device in use", unchecked((int)0x8889000A)))
            .ShouldBe(MicTestState.Failed);
    }

    /// <summary>Makes the next recorder the monitor creates fail to open with <paramref name="failure"/>.</summary>
    private void CreateRecorderThatFails(Exception failure)
    {
        var recorder = Substitute.For<IAudioRecorder>();
        recorder.When(r => r.Start(Arg.Any<string?>())).Throw(failure);
        _prepared.Enqueue(recorder);
    }
}
