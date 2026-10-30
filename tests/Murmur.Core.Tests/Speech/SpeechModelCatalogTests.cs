using Murmur.Core.Models;
using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public sealed class SpeechModelCatalogTests
{
    [Fact]
    public void Get_LargeV3Turbo_IsPinnedToTheCommit()
    {
        var m = SpeechModelCatalog.Get(SpeechModelId.LargeV3Turbo);

        m.Id.ShouldBe(SpeechModelId.LargeV3Turbo);
        m.DisplayName.ShouldBe("Whisper large-v3-turbo");
        m.FileName.ShouldBe("ggml-large-v3-turbo.bin");
        m.SizeBytes.ShouldBe(1_624_555_275);
        m.Sha256.ShouldBe("1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69");
        m.Url.ShouldBe(new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo.bin"));
    }

    [Fact]
    public void Get_SmallEn_IsPinnedToTheCommit()
    {
        var m = SpeechModelCatalog.Get(SpeechModelId.SmallEn);

        m.Id.ShouldBe(SpeechModelId.SmallEn);
        m.DisplayName.ShouldBe("Whisper small (English)");
        m.FileName.ShouldBe("ggml-small.en.bin");
        m.SizeBytes.ShouldBe(487_614_201);
        m.Sha256.ShouldBe("c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d");
        m.Url.ShouldBe(new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small.en.bin"));
    }

    [Theory]
    [InlineData(SpeechModelId.LargeV3Turbo)]
    [InlineData(SpeechModelId.SmallEn)]
    public void Get_EveryModelId_ReturnsAnEntryForThatId(SpeechModelId id)
    {
        SpeechModelCatalog.Get(id).Id.ShouldBe(id);
    }

    [Fact]
    public void Get_UnknownId_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SpeechModelCatalog.Get((SpeechModelId)999));
    }

    [Fact]
    public void Options_LabelsMatchTheSpec()
    {
        SpeechModelCatalog.Options.Select(o => (o.Id, o.Label)).ShouldBe([
            (SpeechModelId.LargeV3Turbo, "Best accuracy (large, 1.6 GB)"),
            (SpeechModelId.SmallEn, "Faster (small, 488 MB)")]);
    }

    [Fact]
    public void Options_CarryTheCatalogSizes()
    {
        SpeechModelCatalog.Options.Select(o => (o.Id, o.SizeBytes)).ShouldBe([
            (SpeechModelId.LargeV3Turbo, SpeechModelCatalog.LargeV3Turbo.SizeBytes),
            (SpeechModelId.SmallEn, SpeechModelCatalog.SmallEn.SizeBytes)]);
    }
}
