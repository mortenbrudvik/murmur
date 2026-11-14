using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Setup;
using Shouldly;

namespace Murmur.Core.Tests.Setup;

public sealed class SplashGateTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly List<SplashCloseReason> _closed = [];

    /// <summary>A gate whose clock is already running, as it is once the splash's animation has begun.</summary>
    private SplashGate CreateGate()
    {
        var gate = CreateIdleGate();
        gate.Start();
        return gate;
    }

    /// <summary>A gate as App creates it with the splash: the clock starts later, when start-up has let go of the UI thread.</summary>
    private SplashGate CreateIdleGate()
    {
        var gate = new SplashGate(_time, SplashGate.DefaultMinimum, SplashGate.DefaultTimeout);
        gate.Closed += _closed.Add;
        return gate;
    }

    /// <summary>
    /// Start-up keeps the UI thread busy for a while after the splash is shown, and nothing animates then. That time
    /// must not count, or the splash would close before its name has been typed.
    /// </summary>
    [Fact]
    public void BeforeStart_TimeDoesNotCount()
    {
        using var gate = CreateIdleGate();
        gate.MarkReady();
        _time.Advance(TimeSpan.FromSeconds(20));
        _closed.ShouldBeEmpty();

        gate.Start();
        _time.Advance(TimeSpan.FromSeconds(2.4));
        _closed.ShouldBeEmpty();

        _time.Advance(TimeSpan.FromSeconds(0.1));
        _closed.ShouldBe([SplashCloseReason.Ready]);
    }

    [Fact]
    public void Skip_BeforeStart_ClosesAtOnce()
    {
        using var gate = CreateIdleGate();

        gate.Skip();

        _closed.ShouldBe([SplashCloseReason.Skipped]);
    }

    [Fact]
    public void Start_Twice_KeepsTheFirstClock()
    {
        using var gate = CreateIdleGate();
        gate.Start();
        _time.Advance(TimeSpan.FromSeconds(2));

        gate.Start();
        gate.MarkReady();
        _time.Advance(TimeSpan.FromSeconds(0.5));

        _closed.ShouldBe([SplashCloseReason.Ready]);
    }

    [Fact]
    public void Start_AfterClosingOrDisposing_DoesNothing()
    {
        var skipped = CreateIdleGate();
        skipped.Skip();
        skipped.Start();
        var disposed = CreateIdleGate();
        disposed.Dispose();
        disposed.Start();

        _time.Advance(TimeSpan.FromSeconds(20));

        _closed.ShouldBe([SplashCloseReason.Skipped]);
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
