using Murmur.Core.Features.App;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Setup;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class AppHandlerTests
{
    [Theory]
    [InlineData(LinkTarget.CudaDownload, "https://developer.nvidia.com/cuda-downloads")]
    [InlineData(LinkTarget.NvidiaDriver, "https://www.nvidia.com/Download/index.aspx")]
    [InlineData(LinkTarget.MicrophonePrivacy, "ms-settings:privacy-microphone")]
    [InlineData(LinkTarget.Repository, "https://github.com/mortenbrudvik/murmur")]
    [InlineData(LinkTarget.PrivacyPolicy, "https://brudvik.com/murmur/privacy")]
    [InlineData(LinkTarget.License, "https://github.com/mortenbrudvik/murmur/blob/main/LICENSE")]
    [InlineData(LinkTarget.HomePage, "https://brudvik.com")]
    public async Task OpenLink_OpensTheWhitelistedUri(LinkTarget target, string uri)
    {
        var opener = Substitute.For<ILinkOpener>();

        await new OpenLinkHandler(opener, new MurmurPaths(@"C:\x")).Handle(new OpenLinkRequest(target), CancellationToken.None);

        opener.Received(1).Open(uri);
    }

    [Fact]
    public async Task OpenLink_DataFolder_CreatesAndOpensThePathsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "murmur-tests-" + Guid.NewGuid().ToString("N"));
        var opener = Substitute.For<ILinkOpener>();
        try
        {
            await new OpenLinkHandler(opener, new MurmurPaths(root)).Handle(new OpenLinkRequest(LinkTarget.DataFolder), CancellationToken.None);

            Directory.Exists(root).ShouldBeTrue();
            opener.Received(1).Open(root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The data folder is a path, not an address: only the handler, which knows the paths, can open it.</summary>
    [Fact]
    public void AppLinks_DataFolder_HasNoFixedAddress()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => AppLinks.For(LinkTarget.DataFolder));
    }

    [Fact]
    public async Task GetAbout_ReturnsAppInfoAndHistoryTotals()
    {
        var app = Substitute.For<IAppInfo>();
        app.Version.Returns("1.1.0");
        app.Channel.Returns("Microsoft Store");
        app.DataFolder.Returns(@"C:\Users\me\AppData\Local\Murmur");
        app.WindowsBuild.Returns("Windows 11, build 26200");
        var since = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);
        var history = Substitute.For<IHistoryStore>();
        history.Totals().Returns(new HistoryTotals(1204, 18420, since));

        var about = await new GetAboutHandler(app, history).Handle(new GetAboutRequest(), CancellationToken.None);

        about.ShouldBe(new AboutInfo(
            "1.1.0", "Microsoft Store", @"C:\Users\me\AppData\Local\Murmur", "Windows 11, build 26200", 1204, 18420, since));
    }

    [Fact]
    public async Task OpenLink_UnknownTarget_OpensNothing()
    {
        var opener = Substitute.For<ILinkOpener>();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => new OpenLinkHandler(opener, new MurmurPaths(@"C:\x")).Handle(new OpenLinkRequest((LinkTarget)99), CancellationToken.None));

        opener.DidNotReceiveWithAnyArgs().Open(default!);
    }

    /// <summary>The page shows "Could not open the link" only when the call fails, so a failure must reach it.</summary>
    [Fact]
    public async Task OpenLink_OpenerThrows_Throws()
    {
        var opener = Substitute.For<ILinkOpener>();
        opener.When(o => o.Open(Arg.Any<string>())).Throw(new InvalidOperationException("no browser"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => new OpenLinkHandler(opener, new MurmurPaths(@"C:\x")).Handle(new OpenLinkRequest(LinkTarget.CudaDownload), CancellationToken.None));
    }

    [Fact]
    public async Task Restart_NewInstanceCannotStart_Throws()
    {
        var lifecycle = Substitute.For<IAppLifecycle>();
        lifecycle.When(l => l.RequestRestart()).Throw(new InvalidOperationException("cannot start"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => new RestartAppHandler(lifecycle).Handle(new RestartAppRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Restart_RequestsARestart()
    {
        var lifecycle = Substitute.For<IAppLifecycle>();

        await new RestartAppHandler(lifecycle).Handle(new RestartAppRequest(), CancellationToken.None);

        lifecycle.Received(1).RequestRestart();
    }

    [Fact]
    public async Task UiReady_MarksTheUiReady()
    {
        var ui = new UiReadiness();

        await new UiReadyHandler(ui).Handle(new UiReadyRequest(), CancellationToken.None);

        ui.IsReady.ShouldBeTrue();
    }

    [Fact]
    public async Task Quit_RequestsAQuit()
    {
        var lifecycle = Substitute.For<IAppLifecycle>();

        await new QuitAppHandler(lifecycle).Handle(new QuitAppRequest(), CancellationToken.None);

        lifecycle.Received(1).RequestQuit();
        lifecycle.DidNotReceive().RequestRestart();
    }

    [Fact]
    public void InputStatus_NewOne_SaysTheHookIsInstalled()
    {
        var status = new InputStatus();

        status.HookInstalled.ShouldBeTrue();
        status.Error.ShouldBeNull();
    }

    [Fact]
    public async Task GetInputStatus_ReportsWhatTheHookStartLeftBehind()
    {
        var status = new InputStatus { HookInstalled = false, Error = "Access is denied" };

        var answer = await new GetInputStatusHandler(status).Handle(new GetInputStatusRequest(), CancellationToken.None);

        answer.HookInstalled.ShouldBeFalse();
        answer.Error.ShouldBe("Access is denied");
    }
}
