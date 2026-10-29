using System.Diagnostics;
using Murmur.Core.Audio;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

/// <summary>
/// Talks to the real Windows audio stack. Tests that need a microphone skip cleanly when none is active.
/// </summary>
public sealed class AudioRecorderIntegrationTests(ITestOutputHelper output)
{
    private void SkipWithoutMicrophone(AudioRecorder recorder)
    {
        var microphones = recorder.ListMicrophones();
        Assert.SkipWhen(microphones.Count == 0, "no microphone");
        output.WriteLine($"Default microphone: {microphones.FirstOrDefault(m => m.IsDefault)?.Name ?? "(none)"}");
    }

    [Fact]
    public void Records2Seconds_FromDefaultMic()
    {
        var recorder = new AudioRecorder();
        SkipWithoutMicrophone(recorder);

        recorder.Start(null);
        float[] samples;
        try
        {
            Thread.Sleep(2000);
        }
        finally
        {
            samples = recorder.Stop();
        }

        output.WriteLine($"Recorded {samples.Length} samples in ~2 s.");
        samples.Length.ShouldBeInRange(32000 - 3200, 32000 + 3200);
    }

    // Diagnostic, run on demand (for example `dotnet test --filter StartLatency` with the mic you care
    // about). It hard-fails on slow microphones such as Bluetooth headsets by design, so it is explicit and
    // stays out of the normal suite.
    [Fact(Explicit = true)]
    public void StartLatency_Under150ms()
    {
        var recorder = new AudioRecorder();
        SkipWithoutMicrophone(recorder);

        var stopwatch = new Stopwatch();
        var firstChunk = new ManualResetEventSlim();
        var elapsedMs = -1.0;
        recorder.SamplesAvailable += _ =>
        {
            if (firstChunk.IsSet)
                return;
            elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            firstChunk.Set();
        };

        stopwatch.Start();
        recorder.Start(null);
        try
        {
            firstChunk.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken).ShouldBeTrue("no audio arrived within 3 s");
        }
        finally
        {
            recorder.Cancel();
        }

        output.WriteLine($"Start latency (Start() to first SamplesAvailable): {elapsedMs:F1} ms");
        elapsedMs.ShouldBeLessThan(150);
    }

    [Fact]
    public void ListMicrophones_HasExactlyOneDefault()
    {
        var recorder = new AudioRecorder();
        SkipWithoutMicrophone(recorder);

        var microphones = recorder.ListMicrophones();

        foreach (var microphone in microphones)
            output.WriteLine($"{(microphone.IsDefault ? "*" : " ")} {microphone.Name} [{microphone.Id}]");
        microphones.Count(m => m.IsDefault).ShouldBe(1);
        microphones.ShouldAllBe(m => m.Id.Length > 0 && m.Name.Length > 0);
    }

    [Fact]
    public void StartWithUnknownDevice_ThrowsAudioDeviceException()
    {
        var recorder = new AudioRecorder();

        Should.Throw<AudioDeviceException>(() => recorder.Start("no-such-device"));
    }

    [Fact]
    public void StartTwice_ThrowsInvalidOperationException()
    {
        var recorder = new AudioRecorder();
        SkipWithoutMicrophone(recorder);

        recorder.Start(null);
        try
        {
            Should.Throw<InvalidOperationException>(() => recorder.Start(null));
        }
        finally
        {
            recorder.Cancel();
        }
    }

    [Fact]
    public void StopOrCancel_WhenNotRecording_IsHarmless()
    {
        var recorder = new AudioRecorder();

        recorder.Stop().ShouldBeEmpty();
        Should.NotThrow(recorder.Cancel);
    }

    [Fact]
    public void Cancel_StopsEventsAndAllowsRestart()
    {
        var recorder = new AudioRecorder();
        SkipWithoutMicrophone(recorder);
        var chunks = 0;
        recorder.SamplesAvailable += _ => Interlocked.Increment(ref chunks);

        recorder.Start(null);
        Thread.Sleep(300);
        recorder.Cancel();
        var afterCancel = Volatile.Read(ref chunks);
        Thread.Sleep(300);

        output.WriteLine($"Chunks before cancel: {afterCancel}");
        Volatile.Read(ref chunks).ShouldBe(afterCancel);

        // The device was released, so a second recording works and starts empty.
        recorder.Start(null);
        recorder.Stop().Length.ShouldBeLessThan(16000);
    }
}
