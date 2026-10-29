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
        var status = new EngineStatus(EngineState.Downloading, "ggml-large-v3-turbo", DownloadProgress: 0.4);
        engine.Status.Returns(status);

        var result = await new GetEngineStatusHandler(engine).Handle(new GetEngineStatusRequest(), CancellationToken.None);

        result.ShouldBe(status);
    }

    [Fact]
    public void EngineStatusChangedNotification_CarriesTheStatus()
    {
        var status = new EngineStatus(EngineState.Ready, "ggml-large-v3-turbo", Runtime: "Cuda");

        new EngineStatusChangedNotification(status).Status.ShouldBe(status);
    }
}
