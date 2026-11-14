using Murmur.Core.Setup;
using Murmur.UI.Splash;
using Shouldly;

namespace Murmur.UI.Tests.Splash;

/// <summary>The splash's last moment: what a request for the window means while it is up, and the hand-over.</summary>
public sealed class SplashSessionTests
{
    private const uint Murmur = 42;
    private const uint AnotherApp = 7;

    private readonly SplashGate _gate = new(TimeProvider.System, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
    private readonly List<SplashCloseReason> _gateClosed = [];
    private readonly List<bool> _reveals = [];
    private int _splashClosed;
    private bool _windowShown = true;
    private uint _foreground = Murmur;
    private Action<bool>? _revealOverride;

    private SplashSession CreateSession()
    {
        _gate.Closed += _gateClosed.Add;
        return new SplashSession(
            _gate,
            mainWindowIsShown: () => _windowShown,
            revealMainWindow: activate =>
            {
                _reveals.Add(activate);
                _revealOverride?.Invoke(activate);
            },
            closeSplash: () => _splashClosed++,
            foregroundProcessId: () => _foreground,
            ownProcessId: Murmur);
    }

    [Fact]
    public void HandOver_MurmurInFront_RevealsTheWindowActivatedAndClosesTheSplash()
    {
        var session = CreateSession();

        session.HandOver();

        _reveals.ShouldBe([true]);
        _splashClosed.ShouldBe(1);
        session.IsOver.ShouldBeTrue();
    }

    [Fact]
    public void HandOver_AnotherAppInFront_RevealsTheWindowWithoutTakingTheFocus()
    {
        _foreground = AnotherApp;
        var session = CreateSession();

        session.HandOver();

        _reveals.ShouldBe([false]);
    }

    /// <summary>
    /// The focus rule protects someone who walked away, not someone who just asked for the window: a click on the tray
    /// icon or a second launch during the splash. The app in front then is the taskbar, Explorer or a terminal, and
    /// without this the window would open behind it.
    /// </summary>
    [Fact]
    public void HandOver_AfterTheWindowWasAskedFor_ActivatesEvenWithAnotherAppInFront()
    {
        _foreground = AnotherApp;
        var session = CreateSession();

        session.RequestWindow();
        session.HandOver();

        _reveals.ShouldBe([true]);
    }

    [Fact]
    public void RequestWindow_CutsTheSplashShort()
    {
        var session = CreateSession();

        session.RequestWindow();

        _gateClosed.ShouldBe([SplashCloseReason.Skipped]);
    }

    [Fact]
    public void Skip_CutsTheSplashShort_AndKeepsTheFocusRule()
    {
        _foreground = AnotherApp;
        var session = CreateSession();

        session.Skip();
        session.HandOver();

        _gateClosed.ShouldBe([SplashCloseReason.Skipped]);
        _reveals.ShouldBe([false]);
    }

    /// <summary>An early skip: start-up has not shown the main window yet. It shows the window itself when it gets there.</summary>
    [Fact]
    public void HandOver_MainWindowNotShownYet_RevealsNothingAndClosesTheSplash()
    {
        _windowShown = false;
        var session = CreateSession();

        session.HandOver();

        _reveals.ShouldBeEmpty();
        _splashClosed.ShouldBe(1);
    }

    /// <summary>
    /// The splash is topmost and nothing else closes it. Symptom if this regresses: a failure while showing the main
    /// window leaves the splash on top of the screen until Murmur is quit.
    /// </summary>
    [Fact]
    public void HandOver_RevealingTheWindowThrows_StillClosesTheSplash()
    {
        _revealOverride = _ => throw new InvalidOperationException("The window is gone.");
        var session = CreateSession();

        Should.Throw<InvalidOperationException>(session.HandOver);

        _splashClosed.ShouldBe(1);
        session.IsOver.ShouldBeTrue();
    }

    [Fact]
    public void HandOver_Twice_HandsOverOnce()
    {
        var session = CreateSession();

        session.HandOver();
        session.HandOver();

        _reveals.Count.ShouldBe(1);
        _splashClosed.ShouldBe(1);
    }

    /// <summary>Quit, or a failed start: the splash goes without its hand-over.</summary>
    [Fact]
    public void Abandon_ClosesTheSplashWithoutRevealingTheWindow_AndALaterHandOverDoesNothing()
    {
        var session = CreateSession();

        session.Abandon();
        session.HandOver();
        session.RequestWindow();

        _reveals.ShouldBeEmpty();
        _splashClosed.ShouldBe(1);
        _gateClosed.ShouldBeEmpty();
        session.IsOver.ShouldBeTrue();
    }

    [Fact]
    public void MarkUiReadyAndBegin_PassOnToTheGate()
    {
        using var gate = new SplashGate(TimeProvider.System, TimeSpan.Zero, TimeSpan.FromMinutes(2));
        var closed = new List<SplashCloseReason>();
        gate.Closed += closed.Add;
        var session = new SplashSession(gate, () => true, _ => { }, () => { }, () => Murmur, Murmur);

        session.MarkUiReady();
        closed.ShouldBeEmpty();

        // With no minimum, the gate closes as soon as its clock has started and its timer has fired.
        session.Begin();
        SpinWait.SpinUntil(() => closed.Count > 0, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        closed.ShouldBe([SplashCloseReason.Ready]);
    }
}
