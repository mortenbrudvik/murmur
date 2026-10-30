using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public sealed class ModelSelectionTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IGpuInfo _gpu = Substitute.For<IGpuInfo>();
    private readonly IModelManager _models = Substitute.For<IModelManager>();

    // What the settings substitute holds; Update applies its change to it.
    private MurmurSettings _stored = MurmurSettings.Default;

    public ModelSelectionTests()
    {
        _settings.Load().Returns(_ => _stored);
        _settings.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>())
            .Returns(ci => _stored = ci.Arg<Func<MurmurSettings, MurmurSettings>>()(_stored));
        _gpu.Report.Returns(GpuReport.Empty);
    }

    private static GpuReport ReportWith(long dedicatedVideoMemory) =>
        new([new GpuAdapter("Some GPU", 0x1002, dedicatedVideoMemory, 1)], null);

    private ModelSelection Selection() => new(_settings, _gpu, _models);

    [Theory]
    [InlineData(4_293_918_720L, SpeechModelId.LargeV3Turbo)] // a "4 GB" card
    [InlineData(12_884_901_888L, SpeechModelId.LargeV3Turbo)]
    [InlineData(2_147_483_648L, SpeechModelId.SmallEn)]
    [InlineData(134_217_728L, SpeechModelId.SmallEn)] // integrated graphics
    public void Recommend_UsesDedicatedVideoMemory(long memory, SpeechModelId expected) =>
        ModelSelection.Recommend(ReportWith(memory)).ShouldBe(expected);

    [Fact]
    public void Recommend_NoAdapters_IsSmall() =>
        ModelSelection.Recommend(GpuReport.Empty).ShouldBe(SpeechModelId.SmallEn);

    [Fact]
    public void Recommend_AnyAdapterIsLargeEnough_IsLarge()
    {
        var report = new GpuReport(
            [new GpuAdapter("Intel UHD", 0x8086, 134_217_728, 1), new GpuAdapter("Radeon", 0x1002, 8_573_157_376, 2)],
            null);

        ModelSelection.Recommend(report).ShouldBe(SpeechModelId.LargeV3Turbo);
    }

    [Theory]
    [InlineData(SpeechModelId.SmallEn, SpeechModelId.LargeV3Turbo, true, SpeechModelId.SmallEn)] // saved wins
    [InlineData(null, SpeechModelId.SmallEn, true, SpeechModelId.LargeV3Turbo)] // 1.0 upgrader keeps large
    [InlineData(null, SpeechModelId.SmallEn, false, SpeechModelId.SmallEn)]
    [InlineData(null, SpeechModelId.LargeV3Turbo, false, SpeechModelId.LargeV3Turbo)]
    public void Resolve_FollowsTheRules(SpeechModelId? saved, SpeechModelId recommended, bool largePresent, SpeechModelId expected) =>
        ModelSelection.Resolve(saved, recommended, largePresent).ShouldBe(expected);

    [Fact]
    public void Recommended_UsesTheDetectedGpu()
    {
        _gpu.Report.Returns(ReportWith(12_884_901_888L));

        Selection().Recommended.ShouldBe(SpeechModelId.LargeV3Turbo);
    }

    [Fact]
    public void ResolveActive_NothingSaved_PersistsTheChoice()
    {
        _models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo).Returns(false);

        var active = Selection().ResolveActive();

        active.ShouldBe(SpeechModelId.SmallEn);
        _settings.Received(1).Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>());
        _stored.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
    }

    [Fact]
    public void ResolveActive_NothingSavedButLargeFilePresent_KeepsLarge()
    {
        _models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo).Returns(true);

        var active = Selection().ResolveActive();

        active.ShouldBe(SpeechModelId.LargeV3Turbo);
        _stored.SpeechModel.ShouldBe(SpeechModelId.LargeV3Turbo);
    }

    [Fact]
    public void ResolveActive_AlreadySaved_DoesNotWrite()
    {
        _stored = MurmurSettings.Default with { SpeechModel = SpeechModelId.SmallEn };
        _gpu.Report.Returns(ReportWith(12_884_901_888L));
        _models.HasExpectedFile(SpeechModelCatalog.LargeV3Turbo).Returns(true);

        var active = Selection().ResolveActive();

        active.ShouldBe(SpeechModelId.SmallEn);
        _settings.DidNotReceiveWithAnyArgs().Update(default!);
        _settings.DidNotReceiveWithAnyArgs().Save(default!);
    }
}
