using Mediator;
using Murmur.Core.Features.App;
using Murmur.Core.Setup;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Setup;

public sealed class UiLauncherTests
{
    private readonly UiReadiness _ui = new();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private int Published => _mediator.ReceivedCalls().Count(call => call.GetArguments()[0] is ShowSettingsNotification);

    [Fact]
    public void ShowSettings_UiReady_PublishesAtOnce()
    {
        _ui.MarkReady();
        var launcher = new UiLauncher(_ui, _mediator);

        launcher.ShowSettings();

        Published.ShouldBe(1);
    }

    [Fact]
    public void ShowSettings_UiNotReady_PublishesWhenItBecomesReady()
    {
        var launcher = new UiLauncher(_ui, _mediator);

        launcher.ShowSettings();
        Published.ShouldBe(0);

        _ui.MarkReady();
        Published.ShouldBe(1);
    }

    [Fact]
    public void ShowSettings_TwiceBeforeReady_PublishesOnce()
    {
        var launcher = new UiLauncher(_ui, _mediator);

        launcher.ShowSettings();
        launcher.ShowSettings();
        _ui.MarkReady();

        Published.ShouldBe(1);
    }

    [Fact]
    public void BecomingReady_WithNothingAsked_PublishesNothing()
    {
        _ = new UiLauncher(_ui, _mediator);

        _ui.MarkReady();

        Published.ShouldBe(0);
    }

    [Fact]
    public void ShowSettings_AgainOnceReady_PublishesEachTime()
    {
        var launcher = new UiLauncher(_ui, _mediator);
        launcher.ShowSettings();
        _ui.MarkReady();

        launcher.ShowSettings();

        Published.ShouldBe(2);
    }
}
