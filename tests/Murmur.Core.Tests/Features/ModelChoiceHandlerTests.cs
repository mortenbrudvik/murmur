using Murmur.Core.Features.Models;
using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class ModelChoiceHandlerTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IGpuInfo _gpu = Substitute.For<IGpuInfo>();
    private readonly IModelManager _models = Substitute.For<IModelManager>();
    private readonly ISpeechEngine _engine = Substitute.For<ISpeechEngine>();

    private MurmurSettings _stored = MurmurSettings.Default;

    public ModelChoiceHandlerTests()
    {
        _settings.Load().Returns(_ => _stored);
        _settings.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>())
            .Returns(ci => _stored = ci.Arg<Func<MurmurSettings, MurmurSettings>>()(_stored));

        // 12 GiB of video memory: this PC suits the large model.
        _gpu.Report.Returns(new GpuReport([new GpuAdapter("Some GPU", 0x10DE, 12_884_901_888L, 1)], null));
    }

    private ModelSelection Selection() => new(_settings, _gpu, _models);

    [Fact]
    public async Task GetModelChoice_ReturnsSelectedRecommendedAndOptions()
    {
        _stored = _stored with { SpeechModel = SpeechModelId.SmallEn };

        var choice = await new GetModelChoiceHandler(Selection()).Handle(new GetModelChoiceRequest(), CancellationToken.None);

        choice.Selected.ShouldBe(SpeechModelId.SmallEn);
        choice.Recommended.ShouldBe(SpeechModelId.LargeV3Turbo);
        choice.Options.ShouldBe(SpeechModelCatalog.Options);
    }

    [Fact]
    public async Task GetModelChoice_NothingChosenYet_SelectsTheRecommendedModel()
    {
        var choice = await new GetModelChoiceHandler(Selection()).Handle(new GetModelChoiceRequest(), CancellationToken.None);

        choice.Selected.ShouldBe(SpeechModelId.LargeV3Turbo);
        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
    }

    [Fact]
    public async Task SetModelChoice_StartsTheSwitchAndReturnsTheNewSelection()
    {
        var choice = await new SetModelChoiceHandler(_engine, Selection())
            .Handle(new SetModelChoiceRequest(SpeechModelId.SmallEn), CancellationToken.None);

        _ = _engine.Received(1).SwitchModelAsync(SpeechModelId.SmallEn);
        choice.Selected.ShouldBe(SpeechModelId.SmallEn);
        choice.Recommended.ShouldBe(SpeechModelId.LargeV3Turbo);
        choice.Options.ShouldBe(SpeechModelCatalog.Options);
    }

    [Fact]
    public async Task SetModelChoice_SwitchStillRunning_ReturnsWithoutWaitingForIt()
    {
        // A switch ends only after the download and the load, which can take minutes.
        var switching = new TaskCompletionSource();
        _engine.SwitchModelAsync(SpeechModelId.SmallEn).Returns(switching.Task);

        var choice = await new SetModelChoiceHandler(_engine, Selection())
            .Handle(new SetModelChoiceRequest(SpeechModelId.SmallEn), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        choice.Selected.ShouldBe(SpeechModelId.SmallEn);
        switching.Task.IsCompleted.ShouldBeFalse();
    }

    /// <summary>The engine could not save the choice: the call fails, so the web says so instead of showing it as made.</summary>
    [Fact]
    public async Task SetModelChoice_TheChoiceCannotBeSaved_Throws()
    {
        _engine.SwitchModelAsync(SpeechModelId.SmallEn)
            .Returns(Task.FromException(new IOException("The settings file is locked.")));

        var ex = await Should.ThrowAsync<IOException>(() => new SetModelChoiceHandler(_engine, Selection())
            .Handle(new SetModelChoiceRequest(SpeechModelId.SmallEn), CancellationToken.None));

        ex.Message.ShouldBe("The settings file is locked.");
    }
}
