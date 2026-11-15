using Murmur.Core.Audio;
using Murmur.Core.Models;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

public sealed class MicrophoneReadinessTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IAudioRecorder _recorder = Substitute.For<IAudioRecorder>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MurmurSettings Settings(bool keepReady, string? microphoneId = null) =>
        MurmurSettings.Default with { KeepMicrophoneReady = keepReady, MicrophoneId = microphoneId };

    [Fact]
    public async Task Apply_SettingOn_KeepsTheRecorderOpenAndOpensTheChosenMicrophone()
    {
        var readiness = new MicrophoneReadiness(_recorder);

        readiness.Apply(Settings(keepReady: true, "mic-1"));
        await readiness.Pending.WaitAsync(Timeout, Ct);

        Received.InOrder(() =>
        {
            _recorder.KeepOpen = true;
            _recorder.Prepare("mic-1");
        });
    }

    [Fact]
    public async Task Apply_SettingOff_StopsKeepingTheRecorderOpenAndOpensNothing()
    {
        var readiness = new MicrophoneReadiness(_recorder);

        readiness.Apply(Settings(keepReady: false));
        await readiness.Pending.WaitAsync(Timeout, Ct);

        _recorder.Received(1).KeepOpen = false;
        _recorder.DidNotReceiveWithAnyArgs().Prepare(default);
    }

    [Fact]
    public async Task Apply_TheMicrophoneCannotBeOpened_DoesNotFault()
    {
        _recorder.When(r => r.Prepare(Arg.Any<string?>())).Throw(new AudioDeviceException("No microphone"));
        var readiness = new MicrophoneReadiness(_recorder);

        readiness.Apply(Settings(keepReady: true));

        await Should.NotThrowAsync(() => readiness.Pending.WaitAsync(Timeout, Ct));
        _recorder.Received(1).KeepOpen = true; // still on: the next dictation opens it, and then it stays open
    }

    [Fact]
    public async Task Apply_WhileAMicrophoneIsStillOpening_ReturnsAtOnceAndAppliesTheNextSettingAfterwards()
    {
        using var opening = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _recorder.When(r => r.Prepare(Arg.Any<string?>())).Do(_ =>
        {
            opening.Set();
            release.Wait(Timeout);
        });
        var readiness = new MicrophoneReadiness(_recorder);
        readiness.Apply(Settings(keepReady: true));
        opening.Wait(Timeout, Ct).ShouldBeTrue();

        readiness.Apply(Settings(keepReady: false)); // returns although the open is still running
        _recorder.DidNotReceive().KeepOpen = false;

        release.Set();
        await readiness.Pending.WaitAsync(Timeout, Ct);

        Received.InOrder(() =>
        {
            _recorder.KeepOpen = true;
            _recorder.Prepare(null);
            _recorder.KeepOpen = false;
        });
    }
}
