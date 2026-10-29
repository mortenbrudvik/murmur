using System.Runtime.InteropServices;
using Murmur.Core.Audio;
using NAudio.Wave;
using Shouldly;

namespace Murmur.Core.Tests.Audio;

/// <summary>Deterministic <see cref="AudioRecorder"/> tests against a fake capture device (no hardware needed).</summary>
public sealed class AudioRecorderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class FakeCaptureDevice(WaveFormat? format = null) : ICaptureDevice
    {
        public string Name => "Fake microphone";

        public WaveFormat WaveFormat { get; } = format ?? WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);

        /// <summary>Runs inside <see cref="StartRecording"/>, to simulate the capture thread starting up.</summary>
        public Action? OnStartRecording { get; init; }

        /// <summary>Runs inside <see cref="Dispose"/>, to simulate joining the capture thread.</summary>
        public Action? OnDispose { get; init; }

        public int DisposeCount { get; private set; }

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording() => OnStartRecording?.Invoke();

        public void Dispose()
        {
            DisposeCount++;
            OnDispose?.Invoke();
        }

        /// <summary>Raises DataAvailable the way the capture thread does, from the calling thread.</summary>
        public void Emit(params float[] interleaved)
        {
            var bytes = new byte[interleaved.Length * sizeof(float)];
            Buffer.BlockCopy(interleaved, 0, bytes, 0, bytes.Length);
            EmitRaw(bytes);
        }

        public void EmitRaw(byte[] bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));

        public void Fail(Exception exception) => RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));

        public void StopCleanly() => RecordingStopped?.Invoke(this, new StoppedEventArgs());
    }

    private static AudioRecorder RecorderWith(params FakeCaptureDevice[] devices)
    {
        var next = 0;
        return new AudioRecorder(_ => devices[next++]);
    }

    [Fact]
    public void Stop_ReturnsEverythingCapturedAndReleasesDevice()
    {
        var device = new FakeCaptureDevice();
        var recorder = RecorderWith(device);
        var raised = new List<float>();
        recorder.SamplesAvailable += chunk => raised.AddRange(chunk);

        recorder.Start(null);
        device.Emit(0.1f, 0.2f);
        device.Emit(0.3f);
        var samples = recorder.Stop();

        samples.ShouldBe([0.1f, 0.2f, 0.3f]);
        raised.ShouldBe([0.1f, 0.2f, 0.3f]);
        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void Stop_StereoFloatAt48k_ReturnsMonoAt16k()
    {
        var device = new FakeCaptureDevice(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var recorder = RecorderWith(device);

        recorder.Start(null);
        for (var chunk = 0; chunk < 10; chunk++)
        {
            // 100 ms of a constant stereo signal: left 0.5, right 0.1 -> mono 0.3.
            var interleaved = new float[4800 * 2];
            for (var frame = 0; frame < 4800; frame++)
            {
                interleaved[frame * 2] = 0.5f;
                interleaved[(frame * 2) + 1] = 0.1f;
            }

            device.Emit(interleaved);
        }

        var samples = recorder.Stop();

        samples.Length.ShouldBeInRange(16000 - 64, 16000 + 64);
        samples[8000].ShouldBe(0.3f, tolerance: 0.01f);
    }

    [Fact]
    public void Stop_Pcm16Device_IsDecoded()
    {
        var device = new FakeCaptureDevice(new WaveFormat(16000, 16, 1));
        var recorder = RecorderWith(device);

        recorder.Start(null);
        // Emit raises the event with float bytes; a PCM16 device sends 16-bit samples instead.
        var pcm = new byte[] { 0x00, 0x40, 0x00, 0xC0 }; // 16384, -16384
        device.EmitRaw(pcm);
        var samples = recorder.Stop();

        samples.ShouldBe([0.5f, -0.5f]);
    }

    [Fact]
    public void Cancel_DiscardsSamplesAndReleasesDevice()
    {
        var device = new FakeCaptureDevice();
        var recorder = RecorderWith(device);

        recorder.Start(null);
        device.Emit(0.1f);
        recorder.Cancel();

        device.DisposeCount.ShouldBe(1);
        recorder.Stop().ShouldBeEmpty();
    }

    [Fact]
    public void AfterStopOrCancel_LateBuffersRaiseNothing()
    {
        var first = new FakeCaptureDevice();
        var second = new FakeCaptureDevice();
        var recorder = RecorderWith(first, second);
        var chunks = 0;
        recorder.SamplesAvailable += _ => chunks++;

        recorder.Start(null);
        recorder.Stop();
        first.Emit(0.1f);
        recorder.Start(null);
        recorder.Cancel();
        second.Emit(0.1f);

        chunks.ShouldBe(0);
    }

    [Fact]
    public void Start_WhileRecording_Throws()
    {
        var recorder = RecorderWith(new FakeCaptureDevice(), new FakeCaptureDevice());
        recorder.Start(null);

        Should.Throw<InvalidOperationException>(() => recorder.Start(null));
    }

    [Fact]
    public void StopAndCancel_WhenNotRecording_AreHarmless()
    {
        var recorder = RecorderWith();

        recorder.Stop().ShouldBeEmpty();
        Should.NotThrow(recorder.Cancel);
    }

    [Fact]
    public void Start_UnsupportedFormat_ThrowsAudioDeviceExceptionAndReleasesDevice()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 24, 2));
        var recorder = RecorderWith(device);

        Should.Throw<AudioDeviceException>(() => recorder.Start(null));

        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void Start_OpenFails_WrapsInAudioDeviceException()
    {
        var comError = new COMException("Element not found", unchecked((int)0x80070490));
        var recorder = new AudioRecorder(_ => throw comError);

        var thrown = Should.Throw<AudioDeviceException>(() => recorder.Start(null));

        thrown.InnerException.ShouldBe(comError);
    }

    [Fact]
    public void Start_StartRecordingFails_ThrowsAudioDeviceExceptionAndRecorderIsReusable()
    {
        var broken = new FakeCaptureDevice { OnStartRecording = () => throw new InvalidOperationException("boom") };
        var working = new FakeCaptureDevice();
        var recorder = RecorderWith(broken, working);

        Should.Throw<AudioDeviceException>(() => recorder.Start(null)).InnerException.ShouldBeOfType<InvalidOperationException>();
        broken.DisposeCount.ShouldBe(1);

        recorder.Start(null);
        working.Emit(0.25f);
        recorder.Stop().ShouldBe([0.25f]);
    }

    [Fact]
    public async Task Start_FailsAfterCaptureThreadIsRunning_DoesNotDeadlock()
    {
        // The capture thread has delivered a buffer (and is waiting for the recorder's lock) by the time
        // StartRecording throws. Releasing the device must not happen under that lock, because disposing
        // the real WasapiCapture joins the capture thread.
        Thread? captureThread = null;
        FakeCaptureDevice? device = null;
        device = new FakeCaptureDevice
        {
            OnStartRecording = () =>
            {
                captureThread = new Thread(() => device!.Emit(0.1f));
                captureThread.Start();
                SpinWait.SpinUntil(() => captureThread.ThreadState.HasFlag(ThreadState.WaitSleepJoin), Timeout);
                throw new InvalidOperationException("device vanished");
            },
            OnDispose = () => captureThread!.Join(),
        };
        var recorder = RecorderWith(device);

        var start = Task.Run(() => Should.Throw<AudioDeviceException>(() => recorder.Start(null)), TestContext.Current.CancellationToken);

        // WaitAsync throws TimeoutException if Start hangs.
        await start.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        captureThread!.Join(Timeout).ShouldBeTrue();
        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void CaptureFailed_RaisedOnceAndRecorderCanStartAgain()
    {
        var failing = new FakeCaptureDevice();
        var next = new FakeCaptureDevice();
        var recorder = RecorderWith(failing, next);
        var failures = new List<Exception>();
        recorder.CaptureFailed += failures.Add;
        var error = new COMException("Device unplugged");

        recorder.Start(null);
        failing.Emit(0.1f);
        failing.Fail(error);
        failing.Fail(new InvalidOperationException("duplicate notification"));

        failures.ShouldBe([error]);
        failing.DisposeCount.ShouldBe(1);
        recorder.Stop().ShouldBeEmpty();
        Should.NotThrow(recorder.Cancel);
        failing.DisposeCount.ShouldBe(1);

        recorder.Start(null);
        next.Emit(0.2f);
        recorder.Stop().ShouldBe([0.2f]);
        failures.Count.ShouldBe(1);
    }

    [Fact]
    public void CaptureFailed_DropsLateBuffersFromTheFailedDevice()
    {
        var device = new FakeCaptureDevice();
        var recorder = RecorderWith(device);
        var chunks = 0;
        recorder.SamplesAvailable += _ => chunks++;

        recorder.Start(null);
        device.Fail(new COMException("Device unplugged"));
        device.Emit(0.1f);

        chunks.ShouldBe(0);
    }

    [Fact]
    public void CaptureFailed_HandlerRunsOutsideTheRecorderLock()
    {
        var device = new FakeCaptureDevice();
        var recorder = RecorderWith(device);
        var otherThreadFinished = false;
        recorder.CaptureFailed += _ =>
        {
            // Another thread uses the recorder while the handler is still running; it would block (and the
            // wait below time out) if the failure were raised under the recorder's lock.
            using var done = new ManualResetEventSlim();
            new Thread(() =>
            {
                recorder.Cancel();
                done.Set();
            }).Start();
            otherThreadFinished = done.Wait(Timeout, TestContext.Current.CancellationToken);
        };

        recorder.Start(null);
        device.Fail(new COMException("Device unplugged"));

        otherThreadFinished.ShouldBeTrue();
    }

    [Fact]
    public void CaptureFailed_ThrowingHandlerDoesNotBreakTheRecorder()
    {
        var failing = new FakeCaptureDevice();
        var next = new FakeCaptureDevice();
        var recorder = RecorderWith(failing, next);
        recorder.CaptureFailed += _ => throw new InvalidOperationException("handler bug");

        recorder.Start(null);
        Should.NotThrow(() => failing.Fail(new COMException("Device unplugged")));

        Should.NotThrow(() => recorder.Start(null));
    }

    [Fact]
    public void RecordingStopped_WithoutError_DoesNotRaiseCaptureFailed()
    {
        var device = new FakeCaptureDevice();
        var recorder = RecorderWith(device);
        var failures = 0;
        recorder.CaptureFailed += _ => failures++;

        recorder.Start(null);
        device.StopCleanly();
        recorder.Stop();

        failures.ShouldBe(0);
    }

    [Fact]
    public void ErrorDuringStopOrCancel_DoesNotRaiseCaptureFailed()
    {
        // Stopping a failing device can itself report an error; the caller already stopped, so stay quiet.
        FakeCaptureDevice? device = null;
        device = new FakeCaptureDevice { OnDispose = () => device!.Fail(new COMException("error while closing")) };
        var recorder = RecorderWith(device);
        var failures = 0;
        recorder.CaptureFailed += _ => failures++;

        recorder.Start(null);
        recorder.Stop();

        failures.ShouldBe(0);
    }
}
