using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Setup;
using Murmur.Core.Speech;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Murmur.Core.Tests.Setup;

public sealed class SetupCoordinatorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IGpuInfo _gpu = Substitute.For<IGpuInfo>();
    private readonly IModelManager _models = Substitute.For<IModelManager>();
    private readonly LaunchOptions _launch = new();

    // What the settings substitute holds; Update applies its change to it. The files on disk are the models in _present.
    private MurmurSettings _stored = MurmurSettings.Default;
    private readonly HashSet<SpeechModelId> _present = [];

    public SetupCoordinatorTests()
    {
        _settings.Load().Returns(_ => _stored);
        _settings.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>())
            .Returns(ci => _stored = ci.Arg<Func<MurmurSettings, MurmurSettings>>()(_stored));
        _gpu.Report.Returns(GpuReport.Empty);
        _models.HasExpectedFile(Arg.Any<SpeechModelInfo>()).Returns(ci => _present.Contains(ci.Arg<SpeechModelInfo>().Id));
    }

    private SetupCoordinator Coordinator() => new(_settings, new ModelSelection(_settings, _gpu, _models), _models, _launch);

    [Fact]
    public async Task Initialize_FreshProfileWithoutModel_StartsSetupAtTheSpeechModelStep()
    {
        var coordinator = Coordinator();

        coordinator.Initialize();
        var state = await coordinator.GetStateAsync();

        state.ShowSetup.ShouldBeTrue();
        state.Step.ShouldBe(SetupStep.SpeechModel);
        _stored.SetupStep.ShouldBe(SetupStep.SpeechModel);
        _stored.SetupCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Initialize_Version10UserWithModel_MarksSetupCompleted()
    {
        // 1.0 never wrote a setup step, and the large model is the only one it downloaded.
        _present.Add(SpeechModelId.LargeV3Turbo);
        var coordinator = Coordinator();

        coordinator.Initialize();
        var state = await coordinator.GetStateAsync();

        state.ShowSetup.ShouldBeFalse();
        _stored.SetupCompleted.ShouldBeTrue();
        _stored.SetupStep.ShouldBeNull();
        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
    }

    [Fact]
    public async Task Initialize_OnlyTheOtherModelsFileIsThere_StartsSetup()
    {
        // The active model is the saved one; the other model's file does not make it present.
        _stored = _stored with { SpeechModel = SpeechModelId.SmallEn };
        _present.Add(SpeechModelId.LargeV3Turbo);
        var coordinator = Coordinator();

        coordinator.Initialize();
        var state = await coordinator.GetStateAsync();

        state.ShowSetup.ShouldBeTrue();
        _stored.SetupStep.ShouldBe(SetupStep.SpeechModel);
    }

    [Fact]
    public async Task Initialize_FirstRunInProgress_ResumesTheSavedStep()
    {
        _stored = _stored with { SetupStep = SetupStep.Microphone, SpeechModel = SpeechModelId.SmallEn };
        _present.Add(SpeechModelId.SmallEn);
        var coordinator = Coordinator();

        coordinator.Initialize();
        var state = await coordinator.GetStateAsync();

        state.ShowSetup.ShouldBeTrue();
        state.Step.ShouldBe(SetupStep.Microphone);
        _stored.SetupCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Initialize_Completed_NeverShowsSetup()
    {
        _stored = _stored with { SetupCompleted = true };
        var before = _stored;
        var coordinator = Coordinator();

        coordinator.Initialize();
        var state = await coordinator.GetStateAsync();

        state.ShowSetup.ShouldBeFalse();
        _stored.ShouldBe(before);
        _settings.DidNotReceive().Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>());
    }

    [Fact]
    public async Task Initialize_CalledTwice_DecidesOnce()
    {
        var coordinator = Coordinator();
        coordinator.Initialize();
        _stored = _stored with { SetupStep = SetupStep.TryIt };
        _models.ClearReceivedCalls();

        coordinator.Initialize();
        var state = await coordinator.GetStateAsync();

        state.Step.ShouldBe(SetupStep.TryIt);
        _models.DidNotReceive().HasExpectedFile(Arg.Any<SpeechModelInfo>());
    }

    [Fact]
    public async Task Initialize_SettingsFail_StillCompletesInitialized()
    {
        _settings.Load().Throws(new IOException("disk"));
        var coordinator = Coordinator();

        Should.Throw<IOException>(coordinator.Initialize);

        await coordinator.Initialized.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetStateAsync_BeforeInitialize_WaitsForIt()
    {
        var coordinator = Coordinator();

        var pending = coordinator.GetStateAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        pending.IsCompleted.ShouldBeFalse();

        coordinator.Initialize();

        var state = await pending.WaitAsync(Patience, TestContext.Current.CancellationToken);
        state.ShowSetup.ShouldBeTrue();
    }

    [Fact]
    public async Task GetStateAsync_OpenPage_IsHandedOutOnce()
    {
        _launch.OpenPage = "settings";
        var coordinator = Coordinator();
        coordinator.Initialize();

        var first = await coordinator.GetStateAsync();
        var second = await coordinator.GetStateAsync();

        first.OpenPage.ShouldBe("settings");
        second.OpenPage.ShouldBeNull();
    }

    [Fact]
    public async Task Update_DoesNotUseUpTheOpenPage()
    {
        _launch.OpenPage = "history";
        var coordinator = Coordinator();
        coordinator.Initialize();

        coordinator.Update(SetupStep.Microphone, null, null).OpenPage.ShouldBeNull();

        (await coordinator.GetStateAsync()).OpenPage.ShouldBe("history");
    }

    [Fact]
    public void Update_PersistsOnlyTheGivenFields()
    {
        _stored = _stored with { SetupStep = SetupStep.Microphone, CudaTipDismissed = true };
        var coordinator = Coordinator();

        var state = coordinator.Update(SetupStep.TryIt, cudaTipDismissed: null, completed: null);

        _stored.SetupStep.ShouldBe(SetupStep.TryIt);
        _stored.CudaTipDismissed.ShouldBeTrue();
        _stored.SetupCompleted.ShouldBeFalse();
        state.ShouldBe(new SetupState(true, SetupStep.TryIt, true, null));
    }

    [Fact]
    public void Update_Completed_HidesSetupAndKeepsTheStep()
    {
        _stored = _stored with { SetupStep = SetupStep.TryIt };
        var coordinator = Coordinator();

        var state = coordinator.Update(null, null, completed: true);

        _stored.SetupCompleted.ShouldBeTrue();
        _stored.SetupStep.ShouldBe(SetupStep.TryIt);
        state.ShowSetup.ShouldBeFalse();
    }

    [Fact]
    public void Update_CudaTipDismissed_IsSaved()
    {
        var coordinator = Coordinator();

        var state = coordinator.Update(null, cudaTipDismissed: true, null);

        _stored.CudaTipDismissed.ShouldBeTrue();
        state.CudaTipDismissed.ShouldBeTrue();
    }
}
