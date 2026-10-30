using Murmur.Core.Features.App;
using Murmur.Core.Models;
using Murmur.Core.Services;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class AppHandlerTests
{
    [Theory]
    [InlineData(LinkTarget.CudaDownload, "https://developer.nvidia.com/cuda-downloads")]
    [InlineData(LinkTarget.NvidiaDriver, "https://www.nvidia.com/Download/index.aspx")]
    [InlineData(LinkTarget.MicrophonePrivacy, "ms-settings:privacy-microphone")]
    public async Task OpenLink_OpensTheWhitelistedUri(LinkTarget target, string uri)
    {
        var opener = Substitute.For<ILinkOpener>();

        await new OpenLinkHandler(opener).Handle(new OpenLinkRequest(target), CancellationToken.None);

        opener.Received(1).Open(uri);
    }

    [Fact]
    public async Task OpenLink_UnknownTarget_OpensNothing()
    {
        var opener = Substitute.For<ILinkOpener>();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => new OpenLinkHandler(opener).Handle(new OpenLinkRequest((LinkTarget)99), CancellationToken.None));

        opener.DidNotReceiveWithAnyArgs().Open(default!);
    }

    [Fact]
    public async Task Restart_RequestsARestart()
    {
        var lifecycle = Substitute.For<IAppLifecycle>();

        await new RestartAppHandler(lifecycle).Handle(new RestartAppRequest(), CancellationToken.None);

        lifecycle.Received(1).RequestRestart();
    }
}
