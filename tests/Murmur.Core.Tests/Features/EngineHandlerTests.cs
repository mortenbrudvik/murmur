using Murmur.Core.Features.Engine;
using Murmur.Core.Models;
using Murmur.Core.Speech;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class EngineHandlerTests
{
    [Fact]
    public async Task GetEngineStatus_ReturnsTheCurrentStatus()
    {
        var engine = Substitute.For<ISpeechEngine>();
        var status = new EngineStatus(EngineState.Downloading, "Whisper large-v3-turbo",
            Download: new DownloadProgress("Whisper large-v3-turbo", 400, 1000, 100, 6, 0), Gpu: GpuAdvice.None);
        engine.Status.Returns(status);

        var result = await new GetEngineStatusHandler(engine).Handle(new GetEngineStatusRequest(), CancellationToken.None);

        result.ShouldBe(status);
    }

    [Fact]
    public void EngineStatusChangedNotification_CarriesTheStatus()
    {
        var status = new EngineStatus(EngineState.Ready, "Whisper large-v3-turbo", Runtime: "Cuda");

        new EngineStatusChangedNotification(status).Status.ShouldBe(status);
    }
}
