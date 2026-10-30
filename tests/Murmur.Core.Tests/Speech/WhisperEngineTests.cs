using System.Globalization;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

/// <summary>
/// <see cref="WhisperEngine"/> with fakes for the model files, the GPU, the CUDA runtime and Whisper itself, so no real
/// model is involved. Downloads that should stay open wait on their cancellation token or on a gate the test opens.
/// </summary>
public sealed class WhisperEngineTests : IDisposable
{
    /// <summary>Real time to wait for something that should take microseconds, so a hang fails instead of freezing the run.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Large = "Whisper large-v3-turbo";
    private const string Small = "Whisper small (English)";
    private const string CudaDirectory = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.2\bin\x64";

    private static readonly GpuReport Rtx3060 = new(
        [new GpuAdapter("NVIDIA GeForce RTX 3060", GpuAdvisor.NvidiaVendorId, 12_884_901_888, 1)],
        new CudaDriver(13000, [new CudaDevice(1, 8, 6)]));

    private static readonly GpuReport Gtx1080 = new(
        [new GpuAdapter("NVIDIA GeForce GTX 1080", GpuAdvisor.NvidiaVendorId, 8_589_934_592, 1)],
        new CudaDriver(13000, [new CudaDevice(1, 6, 1)]));

    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IGpuInfo _gpu = Substitute.For<IGpuInfo>();
    private readonly ICudaRuntimeLocator _cuda = Substitute.For<ICudaRuntimeLocator>();
    private readonly IModelManager _models = Substitute.For<IModelManager>();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeLoader _loader;
    private readonly WhisperEngine _engine;

    private readonly Dictionary<SpeechModelId, Func<IProgress<DownloadProgress>, CancellationToken, Task<string>>> _downloads = [];

    /// <summary>What a model's .part file already holds, where its download reports it starts; 0 when not given.</summary>
    private readonly Dictionary<SpeechModelId, long> _partLengths = [];
    private readonly List<EngineStatus> _statuses = [];
    private readonly List<string> _events = [];

    // What the settings substitute holds; Update applies its change to it.
    private MurmurSettings _stored = MurmurSettings.Default;
    private GpuReport _report = Rtx3060;

    public WhisperEngineTests()
    {
        _settings.Load().Returns(_ => _stored);
        _settings.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>())
            .Returns(ci => _stored = ci.Arg<Func<MurmurSettings, MurmurSettings>>()(_stored));
        _gpu.Report.Returns(_ => _report);
        _cuda.FindRuntimeDirectory().Returns((string?)null);

        _models.PathFor(Arg.Any<SpeechModelInfo>()).Returns(ci => PathOf(ci.Arg<SpeechModelInfo>()));
        _models.EnsureModelAsync(Arg.Any<SpeechModelInfo>(), Arg.Any<IProgress<DownloadProgress>>(), Arg.Any<CancellationToken>())
            .Returns(ci => EnsureAsync(ci.Arg<SpeechModelInfo>(), ci.Arg<IProgress<DownloadProgress>>(), ci.Arg<CancellationToken>()));
        _models.When(m => m.Delete(Arg.Any<SpeechModelInfo>())).Do(ci => Log($"delete:{ci.Arg<SpeechModelInfo>().Id}"));

        _loader = new FakeLoader(Log);
        _engine = new WhisperEngine(_models, new ModelSelection(_settings, _gpu, _models), _gpu, _cuda, _loader, _settings, _time);
        _engine.StatusChanged += status =>
        {
            lock (_statuses)
                _statuses.Add(status);
        };
    }

    public void Dispose() => _engine.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- initialization -----------------------------------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_ModelMissing_ReportsDownloadingLoadingReady()
    {
        await _engine.InitializeAsync(Ct);

        var statuses = Statuses();
        Collapse(statuses).ShouldBe([EngineState.Downloading, EngineState.Loading, EngineState.Ready]);
        statuses.Where(s => s.State == EngineState.Downloading).ShouldAllBe(s =>
            s.ModelName == Large && s.Download!.TotalBytes == SpeechModelCatalog.LargeV3Turbo.SizeBytes);
        statuses.Where(s => s.State != EngineState.Downloading).ShouldAllBe(s => s.Download == null);
        var ready = statuses[^1];
        ready.ModelName.ShouldBe(Large);
        ready.Runtime.ShouldBe("Vulkan");
        ready.Error.ShouldBeNull();
        _loader.Calls.Single().Path.ShouldBe(PathOf(SpeechModelCatalog.LargeV3Turbo));
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedGpu_LoadsWithoutCudaAndNeverSearches()
    {
        _report = Gtx1080;

        await _engine.InitializeAsync(Ct);

        _loader.Calls.Single().ShouldBe((PathOf(SpeechModelCatalog.LargeV3Turbo), false, (string?)null));
        _cuda.DidNotReceive().FindRuntimeDirectory();
        Statuses().ShouldAllBe(s => s.Gpu == GpuAdvice.None);
    }

    [Fact]
    public async Task InitializeAsync_SupportedGpuWithoutRuntime_AdvisesInstallCudaWhileDownloading()
    {
        await _engine.InitializeAsync(Ct);

        var advice = new GpuAdvice(GpuAdviceState.InstallCuda, "NVIDIA GeForce RTX 3060");
        Statuses().First(s => s.State == EngineState.Downloading).Gpu.ShouldBe(advice);
        _engine.Status.Gpu.ShouldBe(advice);
    }

    /// <summary>
    /// CUDA is tried only where the locator found the runtime. It searches the process PATH first, so in production this
    /// changes nothing; the Debug no-CUDA simulation hides the runtime from the locator, and Whisper.net must not find
    /// it on PATH behind its back.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_SupportedGpuWithoutRuntime_NeverTriesCuda()
    {
        await _engine.InitializeAsync(Ct);

        _cuda.Received(1).FindRuntimeDirectory();
        _loader.Calls.Single().ShouldBe((PathOf(SpeechModelCatalog.LargeV3Turbo), false, (string?)null));
    }

    [Fact]
    public async Task InitializeAsync_RuntimeFoundButVulkanLoaded_AdvisesNothing()
    {
        _cuda.FindRuntimeDirectory().Returns(CudaDirectory);

        await _engine.InitializeAsync(Ct);

        _loader.Calls.Single().ShouldBe((PathOf(SpeechModelCatalog.LargeV3Turbo), true, CudaDirectory));
        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.Runtime.ShouldBe("Vulkan");
        _engine.Status.Gpu.ShouldBe(GpuAdvice.None);
    }

    [Fact]
    public async Task InitializeAsync_CudaLoaded_AdvisesCudaInUse()
    {
        _cuda.FindRuntimeDirectory().Returns(CudaDirectory);
        _loader.Runtime = "Cuda";

        await _engine.InitializeAsync(Ct);

        _engine.Status.Runtime.ShouldBe("Cuda");
        _engine.Status.Gpu.ShouldBe(new GpuAdvice(GpuAdviceState.CudaInUse, "NVIDIA GeForce RTX 3060"));
    }

    [Fact]
    public async Task InitializeAsync_DiskFull_ReportsTheSpaceNeeded()
    {
        _downloads[SpeechModelId.LargeV3Turbo] = (_, _) =>
            Task.FromException<string>(new InsufficientDiskSpaceException(1_800_000_000, 900_000_000, @"C:\"));

        // A culture with a decimal comma: the sizes still read like the web's formatBytes.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");
        try
        {
            await _engine.InitializeAsync(Ct);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var status = _engine.Status;
        status.State.ShouldBe(EngineState.Error);
        status.Error.ShouldNotBeNull().ShouldContain("needs 1.8 GB free on C:\\ and there is 0.9 GB");
        status.Error.ShouldBe(
            "Not enough disk space for the speech model: Murmur needs 1.8 GB free on C:\\ and there is 0.9 GB. Free some space and restart Murmur.");
        status.Gpu.ShouldBe(new GpuAdvice(GpuAdviceState.InstallCuda, "NVIDIA GeForce RTX 3060"));
    }

    [Fact]
    public async Task InitializeAsync_DownloadDamagedTwice_AsksForARestart()
    {
        _downloads[SpeechModelId.LargeV3Turbo] = (_, _) =>
            Task.FromException<string>(new ModelVerificationException("The downloaded file does not match its expected SHA-256."));

        await _engine.InitializeAsync(Ct);

        _engine.Status.State.ShouldBe(EngineState.Error);
        _engine.Status.Error.ShouldBe("The speech model download was damaged twice. Restart Murmur to try again.");
    }

    [Fact]
    public async Task InitializeAsync_DownloadFails_EndsInErrorAndDoesNotThrow()
    {
        _downloads[SpeechModelId.LargeV3Turbo] = (_, _) => Task.FromException<string>(
            new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound));

        await _engine.InitializeAsync(Ct);

        _engine.Status.State.ShouldBe(EngineState.Error);
        _engine.Status.Error.ShouldNotBeNull().ShouldContain("download the speech model");
        _engine.Status.Error.ShouldContain("internet connection");
        Collapse(Statuses()).ShouldBe([EngineState.Downloading, EngineState.Error]);
        await Should.ThrowAsync<InvalidOperationException>(() => _engine.TranscribeAsync(Speech(), "", Ct));
    }

    [Fact]
    public async Task InitializeAsync_LoadFails_NamesTheModelFile()
    {
        _loader.Failure = new InvalidOperationException("Failed to load the model.");

        await _engine.InitializeAsync(Ct);

        _engine.Status.State.ShouldBe(EngineState.Error);
        _engine.Status.Error.ShouldNotBeNull().ShouldContain("load the speech model");
        _engine.Status.Error.ShouldContain(PathOf(SpeechModelCatalog.LargeV3Turbo));
        Collapse(Statuses()).ShouldBe([EngineState.Downloading, EngineState.Loading, EngineState.Error]);
    }

    [Fact]
    public async Task InitializeAsync_Cancelled_DoesNotThrowAndLeavesNotStarted()
    {
        _downloads[SpeechModelId.LargeV3Turbo] = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("not cancelled");
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await _engine.InitializeAsync(cts.Token).WaitAsync(Patience, Ct);

        _engine.Status.State.ShouldBe(EngineState.NotStarted);
        _engine.Status.Error.ShouldBeNull();
    }

    [Fact]
    public async Task InitializeAsync_ProgressOnEveryWrite_RaisesAtMostOneStatusPerPercent()
    {
        const int total = 2000;
        _downloads[SpeechModelId.LargeV3Turbo] = (progress, _) =>
        {
            for (var received = 1; received <= total; received++)
                progress.Report(new DownloadProgress(Large, received, total, 1000, 1, 0));
            return Task.FromResult(PathOf(SpeechModelCatalog.LargeV3Turbo));
        };

        await _engine.InitializeAsync(Ct);

        var received = Statuses().Where(s => s.State == EngineState.Downloading).Select(s => s.Download!.BytesReceived).ToList();
        received.Count.ShouldBeLessThanOrEqualTo(102, "the first report, then one per whole percent");
        received.ShouldBeInOrder(SortDirection.Ascending);
        received[^1].ShouldBe(total);
    }

    /// <summary>The manager reports where a resumed download starts, so the engine adds no 0 % of its own.</summary>
    [Fact]
    public async Task InitializeAsync_ResumedDownload_NeverShowsZeroBytes()
    {
        _partLengths[SpeechModelId.LargeV3Turbo] = 1_000_000_000;

        await _engine.InitializeAsync(Ct);

        var downloading = Statuses().Where(s => s.State == EngineState.Downloading).ToList();
        downloading.ShouldNotBeEmpty();
        downloading[0].Download!.BytesReceived.ShouldBe(1_000_000_000);
        downloading.ShouldAllBe(s => s.Download!.BytesReceived > 0);
    }

    [Fact]
    public async Task SwitchModelAsync_ResumedDownload_NeverShowsZeroBytes()
    {
        await ReadyOnLargeAsync();
        _partLengths[SpeechModelId.SmallEn] = 200_000_000;

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        var downloading = Statuses().Where(s => s.Download is not null).ToList();
        downloading.ShouldNotBeEmpty();
        downloading[0].Download!.BytesReceived.ShouldBe(200_000_000);
        downloading.ShouldAllBe(s => s.Download!.BytesReceived > 0);
    }

    [Fact]
    public async Task InitializeAsync_ModelPresent_ReportsLoadingWhileItIsVerified()
    {
        _models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo).Returns(true);
        EngineStatus? whileVerifying = null;
        _downloads[SpeechModelId.LargeV3Turbo] = (_, _) =>
        {
            whileVerifying = _engine.Status;
            return Task.FromResult(PathOf(SpeechModelCatalog.LargeV3Turbo));
        };

        await _engine.InitializeAsync(Ct);

        whileVerifying.ShouldNotBeNull().State.ShouldBe(EngineState.Loading);
        whileVerifying.ModelName.ShouldBe(Large);
        Collapse(Statuses()).ShouldBe([EngineState.Loading, EngineState.Ready]);
    }

    [Fact]
    public async Task InitializeAsync_NoSavedChoiceAndNoGpu_UsesTheSmallModel()
    {
        _report = GpuReport.Empty;

        await _engine.InitializeAsync(Ct);

        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
        _loader.Calls.Single().ShouldBe((PathOf(SpeechModelCatalog.SmallEn), false, (string?)null));
        _cuda.DidNotReceive().FindRuntimeDirectory();
    }

    /// <summary>Murmur quit while a switch downloaded: the next start loads the new choice and deletes the previous model.</summary>
    [Fact]
    public async Task InitializeAsync_AnotherModelOnDisk_IsDeletedOnceReady()
    {
        _stored = _stored with { SpeechModel = SpeechModelId.SmallEn };
        _models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo).Returns(true);
        EngineState? stateWhenDeleted = null;
        _models.When(m => m.Delete(SpeechModelCatalog.LargeV3Turbo)).Do(_ => stateWhenDeleted = _engine.Status.State);

        await _engine.InitializeAsync(Ct);

        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _models.Received(1).Delete(SpeechModelCatalog.LargeV3Turbo);
        _models.DidNotReceive().Delete(SpeechModelCatalog.SmallEn);
        Index("delete:LargeV3Turbo").ShouldBeGreaterThan(Index("load:ggml-small.en.bin"));
        stateWhenDeleted.ShouldBe(EngineState.Ready);
    }

    [Fact]
    public async Task InitializeAsync_DeletingAnotherModelFails_StillEndsReady()
    {
        _models.When(m => m.Delete(SpeechModelCatalog.SmallEn)).Do(_ => throw new IOException("The file is in use."));

        await _engine.InitializeAsync(Ct);

        _models.Received(1).Delete(SpeechModelCatalog.SmallEn);
        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Large);
        _engine.Status.Error.ShouldBeNull();
        Statuses().ShouldNotContain(s => s.State == EngineState.Error);
    }

    // ---- transcription ------------------------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_BeforeInitialize_Throws()
    {
        _engine.Status.State.ShouldBe(EngineState.NotStarted);

        await Should.ThrowAsync<InvalidOperationException>(() => _engine.TranscribeAsync(Speech(), "", Ct));
    }

    [Fact]
    public async Task TranscribeAsync_Ready_UsesTheLoadedModel()
    {
        await ReadyOnLargeAsync();

        var result = await _engine.TranscribeAsync(Speech(), "Vocabulary: Murmur.", Ct);

        result.Text.ShouldBe("from ggml-large-v3-turbo.bin");
        _loader.ModelAt(0).LastPrompt.ShouldBe("Vocabulary: Murmur.");
    }

    // Whisper answers "Thank you." for zeros, so the engine answers for a dead input itself.
    [Fact]
    public async Task TranscribeAsync_DigitalSilence_ReturnsNoSpeechWithoutRunningTheModel()
    {
        await ReadyOnLargeAsync();

        var result = await _engine.TranscribeAsync(new float[3 * 16000], "", Ct);

        result.ShouldBe(new TranscriptionResult("", 1f));
        _loader.ModelAt(0).Transcriptions.ShouldBe(0);
    }

    // ---- switching models ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SwitchModelAsync_WhileReady_StaysReadyUntilTheNewModelIsLoaded()
    {
        await ReadyOnLargeAsync();

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        var statuses = Statuses();
        var downloading = statuses.TakeWhile(s => s.State == EngineState.Ready && s.Download is not null).ToList();
        downloading.ShouldNotBeEmpty();
        downloading.ShouldAllBe(s => s.ModelName == Large && s.Runtime == "Vulkan"
            && s.Download!.ModelName == Small && s.Download.TotalBytes == SpeechModelCatalog.SmallEn.SizeBytes);
        statuses.Skip(downloading.Count).Select(s => (s.State, s.ModelName)).ShouldBe([(EngineState.Loading, Small), (EngineState.Ready, Small)]);
        statuses[^1].Download.ShouldBeNull();
        statuses[^1].Runtime.ShouldBe("Vulkan");

        _loader.ModelAt(0).Disposed.ShouldBeTrue();
        _loader.ModelAt(1).Disposed.ShouldBeFalse();
        _models.Received(1).Delete(SpeechModelCatalog.LargeV3Turbo);
        _models.DidNotReceive().Delete(SpeechModelCatalog.SmallEn);
        Index("delete:LargeV3Turbo").ShouldBeGreaterThan(Index("load:ggml-small.en.bin"));
        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
        (await _engine.TranscribeAsync(Speech(), "", Ct)).Text.ShouldBe("from ggml-small.en.bin");
    }

    [Fact]
    public async Task SwitchModelAsync_DuringTheFirstDownload_CancelsItAndLoadsTheNewModel()
    {
        var largeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawCancellation = false;
        _downloads[SpeechModelId.LargeV3Turbo] = async (progress, ct) =>
        {
            progress.Report(new DownloadProgress(Large, 1000, SpeechModelCatalog.LargeV3Turbo.SizeBytes, 1000, null, 0));
            largeStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                sawCancellation = true;
                throw;
            }

            throw new InvalidOperationException("not cancelled");
        };
        var initializing = _engine.InitializeAsync(Ct);
        await largeStarted.Task.WaitAsync(Patience, Ct);
        _engine.Status.State.ShouldBe(EngineState.Downloading);

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);
        await initializing.WaitAsync(Patience, Ct);

        sawCancellation.ShouldBeTrue();
        _models.Received().Delete(SpeechModelCatalog.LargeV3Turbo);
        Index("delete:LargeV3Turbo").ShouldBeGreaterThan(Index("ensured:LargeV3Turbo"), "the download let go of its .part first");
        Index("ensure:SmallEn").ShouldBeGreaterThan(Index("delete:LargeV3Turbo"));
        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _loader.Calls.Single().Path.ShouldBe(PathOf(SpeechModelCatalog.SmallEn));
        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);

        var statuses = Statuses();
        statuses.ShouldNotContain(s => s.State == EngineState.NotStarted);
        statuses.SkipWhile(s => s.ModelName == Large).ShouldAllBe(s => s.ModelName == Small);
    }

    [Fact]
    public async Task SwitchModelAsync_WaitsForARunningTranscriptionBeforeDisposingTheOldModel()
    {
        await ReadyOnLargeAsync();
        var old = _loader.ModelAt(0);
        old.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transcribing = _engine.TranscribeAsync(Speech(), "", Ct);
        await old.Started.Task.WaitAsync(Patience, Ct);

        var switching = _engine.SwitchModelAsync(SpeechModelId.SmallEn);
        await WaitUntilAsync(() => _loader.LoadCount == 2, "the new model to load");
        await Task.WhenAny(switching, Task.Delay(100, Ct)); // room for the switch to run ahead if it would
        switching.IsCompleted.ShouldBeFalse();
        old.Disposed.ShouldBeFalse();

        old.Gate.SetResult();
        (await transcribing.WaitAsync(Patience, Ct)).Text.ShouldBe("from ggml-large-v3-turbo.bin");
        await switching.WaitAsync(Patience, Ct);

        old.Disposed.ShouldBeTrue();
        _engine.Status.ModelName.ShouldBe(Small);
    }

    [Fact]
    public async Task SwitchModelAsync_DownloadFails_StaysReadyOnTheOldModelAndRestoresTheChoice()
    {
        await ReadyOnLargeAsync();
        _downloads[SpeechModelId.SmallEn] = (progress, _) =>
        {
            progress.Report(new DownloadProgress(Small, 1000, SpeechModelCatalog.SmallEn.SizeBytes, 1000, null, 0));
            return Task.FromException<string>(
                new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound));
        };

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        var status = _engine.Status;
        status.State.ShouldBe(EngineState.Ready);
        status.ModelName.ShouldBe(Large);
        status.Runtime.ShouldBe("Vulkan");
        status.Download.ShouldBeNull();
        status.Error.ShouldNotBeNull().ShouldStartWith("Could not switch to Whisper small (English): ");
        status.Error.ShouldContain("404");
        Statuses().ShouldAllBe(s => s.State == EngineState.Ready);
        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
        _loader.ModelAt(0).Disposed.ShouldBeFalse();
        _models.DidNotReceive().Delete(SpeechModelCatalog.LargeV3Turbo);
        (await _engine.TranscribeAsync(Speech(), "", Ct)).Text.ShouldBe("from ggml-large-v3-turbo.bin");
    }

    [Fact]
    public async Task SwitchModelAsync_LoadFails_StaysReadyOnTheOldModelAndRestoresTheChoice()
    {
        await ReadyOnLargeAsync();
        _loader.Failure = new InvalidOperationException("Out of video memory.");

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Large);
        _engine.Status.Error.ShouldBe("Could not switch to Whisper small (English): Out of video memory.");
        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
        _loader.ModelAt(0).Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task SwitchModelAsync_PersistsTheChoiceBeforeItReturns()
    {
        await ReadyOnLargeAsync();
        _downloads[SpeechModelId.SmallEn] = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("not cancelled");
        };

        var switching = _engine.SwitchModelAsync(SpeechModelId.SmallEn);

        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
        switching.IsCompleted.ShouldBeFalse();
    }

    /// <summary>
    /// ApiBridge runs each request on the thread pool, so a request can save its choice and then reach the operation
    /// gate after a later request. Running the older switch last would leave a model running that is not the choice.
    /// </summary>
    [Fact]
    public async Task SwitchModelAsync_OlderChoiceReachesTheGateLast_EndsOnTheNewerChoice()
    {
        await ReadyOnLargeAsync();
        _engine.TrySaveChoice(SpeechModelId.LargeV3Turbo, out var large, out var largeVersion).ShouldBeTrue();
        _engine.TrySaveChoice(SpeechModelId.SmallEn, out var small, out var smallVersion).ShouldBeTrue();

        await _engine.SwitchToChoiceAsync(small, smallVersion).WaitAsync(Patience, Ct);
        await _engine.SwitchToChoiceAsync(large, largeVersion).WaitAsync(Patience, Ct);

        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _loader.LoadCount.ShouldBe(2);
        (await _engine.TranscribeAsync(Speech(), "", Ct)).Text.ShouldBe("from ggml-small.en.bin");
    }

    [Fact]
    public async Task SwitchModelAsync_OlderSwitchAwayReachesTheGateLast_StaysOnTheNewerChoice()
    {
        await ReadyOnLargeAsync();
        _engine.TrySaveChoice(SpeechModelId.SmallEn, out var small, out var smallVersion).ShouldBeTrue();
        _engine.TrySaveChoice(SpeechModelId.LargeV3Turbo, out var large, out var largeVersion).ShouldBeTrue();

        await _engine.SwitchToChoiceAsync(large, largeVersion).WaitAsync(Patience, Ct);
        await _engine.SwitchToChoiceAsync(small, smallVersion).WaitAsync(Patience, Ct);

        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Large);
        _loader.LoadCount.ShouldBe(1);
        await _models.DidNotReceive().EnsureModelAsync(SpeechModelCatalog.SmallEn, Arg.Any<IProgress<DownloadProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchModelAsync_ActiveModelWhileReady_OnlyPersistsTheChoice()
    {
        await ReadyOnLargeAsync();
        _stored = _stored with { SpeechModel = SpeechModelId.SmallEn };

        await _engine.SwitchModelAsync(SpeechModelId.LargeV3Turbo).WaitAsync(Patience, Ct);

        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
        Statuses().ShouldBeEmpty();
        _loader.LoadCount.ShouldBe(1);
        await _models.DidNotReceive().EnsureModelAsync(Arg.Any<SpeechModelInfo>(), Arg.Any<IProgress<DownloadProgress>>(), Arg.Any<CancellationToken>());
        _models.DidNotReceive().Delete(Arg.Any<SpeechModelInfo>());
    }

    [Fact]
    public async Task SwitchModelAsync_BackToTheLoadedModelDuringASwitch_CancelsTheDownloadAndStaysReady()
    {
        await ReadyOnLargeAsync();
        var smallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawCancellation = false;
        _downloads[SpeechModelId.SmallEn] = async (_, ct) =>
        {
            smallStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                sawCancellation = true;
                throw;
            }

            throw new InvalidOperationException("not cancelled");
        };
        var toSmall = _engine.SwitchModelAsync(SpeechModelId.SmallEn);
        await smallStarted.Task.WaitAsync(Patience, Ct);

        await _engine.SwitchModelAsync(SpeechModelId.LargeV3Turbo).WaitAsync(Patience, Ct);
        await toSmall.WaitAsync(Patience, Ct);

        sawCancellation.ShouldBeTrue();
        _engine.Status.ShouldBe(new EngineStatus(EngineState.Ready, Large, "Vulkan", Gpu: new GpuAdvice(GpuAdviceState.InstallCuda, "NVIDIA GeForce RTX 3060")));
        _models.Received(1).Delete(SpeechModelCatalog.SmallEn);
        _models.DidNotReceive().Delete(SpeechModelCatalog.LargeV3Turbo);
        _loader.ModelAt(0).Disposed.ShouldBeFalse();
        _loader.LoadCount.ShouldBe(1);
        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
    }

    [Fact]
    public async Task SwitchModelAsync_WhileAPresentModelIsVerified_DeletesItOnlyOnceTheNewModelIsLoaded()
    {
        _models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo).Returns(true);
        var verifying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloads[SpeechModelId.LargeV3Turbo] = async (_, ct) =>
        {
            verifying.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("not cancelled");
        };
        var initializing = _engine.InitializeAsync(Ct);
        await verifying.Task.WaitAsync(Patience, Ct);

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);
        await initializing.WaitAsync(Patience, Ct);

        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _models.Received(1).Delete(SpeechModelCatalog.LargeV3Turbo);
        Index("delete:LargeV3Turbo").ShouldBeGreaterThan(Index("load:ggml-small.en.bin"));
    }

    [Fact]
    public async Task SwitchModelAsync_AfterAFailedStart_LoadsTheNewModel()
    {
        _downloads[SpeechModelId.LargeV3Turbo] = (_, _) => Task.FromException<string>(
            new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound));
        await _engine.InitializeAsync(Ct);
        _engine.Status.State.ShouldBe(EngineState.Error);

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _engine.Status.Error.ShouldBeNull();
        _models.Received().Delete(SpeechModelCatalog.LargeV3Turbo);
        _models.DidNotReceive().Delete(SpeechModelCatalog.SmallEn);
    }

    [Fact]
    public async Task SwitchModelAsync_BeforeInitialize_OnlyPersistsTheChoice()
    {
        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
        _engine.Status.State.ShouldBe(EngineState.NotStarted);
        _loader.LoadCount.ShouldBe(0);

        await _engine.InitializeAsync(Ct);

        _engine.Status.ModelName.ShouldBe(Small);
    }

    /// <summary>
    /// The complete large file is kept while no model is loaded, through a failed switch to small, and goes once a
    /// retry of small is Ready.
    /// </summary>
    [Fact]
    public async Task SwitchModelAsync_FirstLoadFailsThenRetried_LeavesNoOtherModel()
    {
        _models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo).Returns(true);
        var verifying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloads[SpeechModelId.LargeV3Turbo] = async (_, ct) =>
        {
            verifying.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("not cancelled");
        };
        var smallAttempts = 0;
        _downloads[SpeechModelId.SmallEn] = (_, _) => Interlocked.Increment(ref smallAttempts) == 1
            ? Task.FromException<string>(new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound))
            : Task.FromResult(PathOf(SpeechModelCatalog.SmallEn));
        var initializing = _engine.InitializeAsync(Ct);
        await verifying.Task.WaitAsync(Patience, Ct);

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);
        await initializing.WaitAsync(Patience, Ct);
        _engine.Status.State.ShouldBe(EngineState.Error);
        _models.DidNotReceive().Delete(SpeechModelCatalog.LargeV3Turbo);

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _models.Received(1).Delete(SpeechModelCatalog.LargeV3Turbo);
        _models.DidNotReceive().Delete(SpeechModelCatalog.SmallEn);
        Index("delete:LargeV3Turbo").ShouldBeGreaterThan(Index("load:ggml-small.en.bin"));
    }

    [Fact]
    public async Task SwitchModelAsync_DeletingThePreviousModelFails_StillEndsReady()
    {
        await ReadyOnLargeAsync();
        _models.When(m => m.Delete(SpeechModelCatalog.LargeV3Turbo)).Do(_ => throw new UnauthorizedAccessException("Access is denied."));

        await _engine.SwitchModelAsync(SpeechModelId.SmallEn).WaitAsync(Patience, Ct);

        _models.Received(1).Delete(SpeechModelCatalog.LargeV3Turbo);
        _engine.Status.State.ShouldBe(EngineState.Ready);
        _engine.Status.ModelName.ShouldBe(Small);
        _engine.Status.Error.ShouldBeNull();
        _loader.ModelAt(0).Disposed.ShouldBeTrue();
        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private async Task ReadyOnLargeAsync()
    {
        await _engine.InitializeAsync(Ct);
        _engine.Status.State.ShouldBe(EngineState.Ready, _engine.Status.Error);
        _engine.Status.ModelName.ShouldBe(Large);
        lock (_statuses)
            _statuses.Clear();
        lock (_events)
            _events.Clear();
        _models.ClearReceivedCalls();
    }

    private async Task<string> EnsureAsync(SpeechModelInfo model, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        Log($"ensure:{model.Id}");
        try
        {
            // Like ModelManager: a download first reports where it starts, which is what its .part file already holds.
            if (!_models.HasExpectedFile(model))
                progress.Report(new DownloadProgress(model.DisplayName, _partLengths.GetValueOrDefault(model.Id), model.SizeBytes, 0, null, 0));

            if (_downloads.TryGetValue(model.Id, out var download))
                return await download(progress, ct);

            // Two reports, as a real download makes many.
            progress.Report(new DownloadProgress(model.DisplayName, model.SizeBytes / 2, model.SizeBytes, 1_000_000, 10, 0));
            progress.Report(new DownloadProgress(model.DisplayName, model.SizeBytes, model.SizeBytes, 1_000_000, 0, 0));
            return PathOf(model);
        }
        finally
        {
            Log($"ensured:{model.Id}");
        }
    }

    private static string PathOf(SpeechModelInfo model) => Path.Combine(@"C:\Murmur\models", model.FileName);

    /// <summary>A second of a 440 Hz tone: loud enough to be no digital silence.</summary>
    private static float[] Speech() =>
        Enumerable.Range(0, 16000).Select(i => 0.1f * MathF.Sin(2 * MathF.PI * 440 * i / 16000)).ToArray();

    private List<EngineStatus> Statuses()
    {
        lock (_statuses)
            return [.. _statuses];
    }

    /// <summary>The states in the order they were entered, each run of the same state counted once.</summary>
    private static List<EngineState> Collapse(IEnumerable<EngineStatus> statuses)
    {
        var states = new List<EngineState>();
        foreach (var status in statuses)
        {
            if (states.Count == 0 || states[^1] != status.State)
                states.Add(status.State);
        }

        return states;
    }

    private void Log(string entry)
    {
        lock (_events)
            _events.Add(entry);
    }

    private int Index(string entry)
    {
        lock (_events)
        {
            var index = _events.IndexOf(entry);
            index.ShouldBeGreaterThanOrEqualTo(0, $"\"{entry}\" in [{string.Join(", ", _events)}]");
            return index;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for {what}.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class FakeLoader(Action<string> log) : IWhisperModelLoader
    {
        private readonly Lock _lock = new();
        private readonly List<(string Path, bool CanUseCuda, string? CudaDirectory)> _calls = [];
        private readonly List<FakeLoadedModel> _models = [];

        public string Runtime { get; set; } = "Vulkan";

        public Exception? Failure { get; set; }

        public IReadOnlyList<(string Path, bool CanUseCuda, string? CudaDirectory)> Calls
        {
            get
            {
                lock (_lock)
                    return [.. _calls];
            }
        }

        public int LoadCount
        {
            get
            {
                lock (_lock)
                    return _calls.Count;
            }
        }

        public FakeLoadedModel ModelAt(int index)
        {
            lock (_lock)
                return _models[index];
        }

        public Task<ILoadedSpeechModel> LoadAsync(string modelPath, bool canUseCuda, string? cudaRuntimeDirectory, CancellationToken ct)
        {
            log($"load:{Path.GetFileName(modelPath)}");
            lock (_lock)
                _calls.Add((modelPath, canUseCuda, cudaRuntimeDirectory));
            if (Failure is { } failure)
                return Task.FromException<ILoadedSpeechModel>(failure);

            var model = new FakeLoadedModel(modelPath, Runtime);
            lock (_lock)
                _models.Add(model);
            return Task.FromResult<ILoadedSpeechModel>(model);
        }
    }

    private sealed class FakeLoadedModel(string path, string runtime) : ILoadedSpeechModel
    {
        private int _transcriptions;
        private volatile bool _disposed;

        public string? Runtime => runtime;

        /// <summary>When set, transcriptions wait for it.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed => _disposed;

        public int Transcriptions => Volatile.Read(ref _transcriptions);

        public string? LastPrompt { get; private set; }

        public async Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Interlocked.Increment(ref _transcriptions);
            LastPrompt = prompt;
            Started.TrySetResult();
            if (Gate is { } gate)
                await gate.Task.WaitAsync(ct);

            ObjectDisposedException.ThrowIf(_disposed, this);
            return new TranscriptionResult($"from {Path.GetFileName(path)}", 0f);
        }

        public void Dispose() => _disposed = true;
    }
}
