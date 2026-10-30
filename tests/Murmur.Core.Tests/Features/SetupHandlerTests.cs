using Murmur.Core.Features.Setup;
using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Setup;
using Murmur.Core.Speech;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class SetupHandlerTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IModelManager _models = Substitute.For<IModelManager>();
    private readonly IGpuInfo _gpu = Substitute.For<IGpuInfo>();

    private MurmurSettings _stored = MurmurSettings.Default;

    public SetupHandlerTests()
    {
        _settings.Load().Returns(_ => _stored);
        _settings.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>())
            .Returns(ci => _stored = ci.Arg<Func<MurmurSettings, MurmurSettings>>()(_stored));
        _gpu.Report.Returns(GpuReport.Empty);
    }

    private SetupCoordinator Coordinator() =>
        new(_settings, new ModelSelection(_settings, _gpu, _models), _models, new LaunchOptions { OpenPage = "settings" });

    [Fact]
    public async Task GetSetupState_ReturnsTheDecidedState()
    {
        var coordinator = Coordinator();
        coordinator.Initialize();

        var state = await new GetSetupStateHandler(coordinator).Handle(new GetSetupStateRequest(), CancellationToken.None);

        state.ShouldBe(new SetupState(true, SetupStep.SpeechModel, false, "settings"));
    }

    [Fact]
    public async Task UpdateSetupState_SavesTheGivenFieldsAndReturnsTheState()
    {
        var coordinator = Coordinator();
        coordinator.Initialize();

        var state = await new UpdateSetupStateHandler(coordinator).Handle(
            new UpdateSetupStateRequest(SetupStep.TryIt, CudaTipDismissed: true, Completed: null), CancellationToken.None);

        state.ShouldBe(new SetupState(true, SetupStep.TryIt, true, null));
        _stored.SetupStep.ShouldBe(SetupStep.TryIt);
        _stored.CudaTipDismissed.ShouldBeTrue();
    }
}
