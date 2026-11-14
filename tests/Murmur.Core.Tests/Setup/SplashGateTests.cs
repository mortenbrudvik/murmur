using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Setup;
using Shouldly;

namespace Murmur.Core.Tests.Setup;

public sealed class SplashGateTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly List<SplashCloseReason> _closed = [];

    private SplashGate CreateGate()
    {
        var gate = new SplashGate(_time, SplashGate.DefaultMinimum, SplashGate.DefaultTimeout);
        gate.Closed += _closed.Add;
        return gate;
    }

    [Fact]
    public void Defaults_AreTwoAndAHalfAndEightSeconds()
    {
        SplashGate.DefaultMinimum.ShouldBe(TimeSpan.FromSeconds(2.5));
        SplashGate.DefaultTimeout.ShouldBe(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void ReadyBeforeTheMinimum_ClosesAtTheMinimum_AsReady()
    {
        using var gate = CreateGate();
        _time.Advance(TimeSpan.FromSeconds(1));

        gate.MarkReady();
        _time.Advance(TimeSpan.FromSeconds(1.4));
        _closed.ShouldBeEmpty();

        _time.Advance(TimeSpan.FromSeconds(0.1));
        _closed.ShouldBe([SplashCloseReason.Ready]);
    }

    [Fact]
    public void ReadyAfterTheMinimum_ClosesAtOnce_AsReady()
    {
        using var gate = CreateGate();
        _time.Advance(TimeSpan.FromSeconds(3));
        _closed.ShouldBeEmpty();

        gate.MarkReady();

        _closed.ShouldBe([SplashCloseReason.Ready]);
    }

    [Fact]
    public void Skip_ClosesAtOnce_AsSkipped()
    {
        using var gate = CreateGate();
        _time.Advance(TimeSpan.FromSeconds(0.2));

        gate.Skip();

        _closed.ShouldBe([SplashCloseReason.Skipped]);
    }

    [Fact]
    public void NeverReady_ClosesAtTheTimeout_AsTimedOut()
    {
        using var gate = CreateGate();

        _time.Advance(TimeSpan.FromSeconds(7.9));
        _closed.ShouldBeEmpty();

        _time.Advance(TimeSpan.FromSeconds(0.1));
        _closed.ShouldBe([SplashCloseReason.TimedOut]);
    }

    [Fact]
    public void Skip_AfterReadyButBeforeTheMinimum_ClosesAsSkipped()
    {
        using var gate = CreateGate();
        _time.Advance(TimeSpan.FromSeconds(1));
        gate.MarkReady();

        gate.Skip();

        _closed.ShouldBe([SplashCloseReason.Skipped]);
    }

    [Fact]
    public void Closed_IsRaisedOnlyOnce()
    {
        using var gate = CreateGate();
        _time.Advance(TimeSpan.FromSeconds(3));

        gate.MarkReady();
        gate.Skip();
        gate.MarkReady();
        _time.Advance(TimeSpan.FromSeconds(10));

        _closed.ShouldBe([SplashCloseReason.Ready]);
    }

    [Fact]
    public void Dispose_BeforeClosing_NeverRaisesClosed()
    {
        var gate = CreateGate();
        _time.Advance(TimeSpan.FromSeconds(1));

        gate.Dispose();
        gate.MarkReady();
        gate.Skip();
        _time.Advance(TimeSpan.FromSeconds(10));

        _closed.ShouldBeEmpty();
    }

    [Fact]
    public void IsClosed_FollowsTheGate()
    {
        using var gate = CreateGate();
        gate.IsClosed.ShouldBeFalse();

        gate.Skip();

        gate.IsClosed.ShouldBeTrue();
    }
}
