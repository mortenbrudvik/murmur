using MediatR;
using Murmur.Core.Features.App;
using Murmur.Core.Setup;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Setup;

public sealed class AboutLauncherTests
{
    private readonly UiReadiness _ui = new();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private int Published => _mediator.ReceivedCalls().Count(call => call.GetArguments()[0] is ShowAboutNotification);

    [Fact]
    public void Show_UiReady_PublishesAtOnce()
    {
        _ui.MarkReady();
        var launcher = new AboutLauncher(_ui, _mediator);

        launcher.Show();

        Published.ShouldBe(1);
    }

    [Fact]
    public void Show_UiNotReady_PublishesWhenItBecomesReady()
    {
        var launcher = new AboutLauncher(_ui, _mediator);

        launcher.Show();
        Published.ShouldBe(0);

        _ui.MarkReady();
        Published.ShouldBe(1);
    }

    [Fact]
    public void Show_TwiceBeforeReady_PublishesOnce()
    {
        var launcher = new AboutLauncher(_ui, _mediator);

        launcher.Show();
        launcher.Show();
        _ui.MarkReady();

        Published.ShouldBe(1);
    }

    [Fact]
    public void BecomingReady_WithNothingAsked_PublishesNothing()
    {
        _ = new AboutLauncher(_ui, _mediator);

        _ui.MarkReady();

        Published.ShouldBe(0);
    }

    [Fact]
    public void Show_AgainOnceReady_PublishesEachTime()
    {
        var launcher = new AboutLauncher(_ui, _mediator);
        launcher.Show();
        _ui.MarkReady();

        launcher.Show();

        Published.ShouldBe(2);
    }
}
