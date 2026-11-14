using System.ComponentModel;
using Shouldly;

namespace Murmur.UI.Tests;

public class AppLifecycleTests
{
    private readonly List<string> _calls = [];

    private AppLifecycle Lifecycle(Action? startNewInstance = null)
    {
        var lifecycle = new AppLifecycle(startNewInstance ?? (() => _calls.Add("start")));
        lifecycle.RestartRequested += () => _calls.Add("restartRequested");
        return lifecycle;
    }

    [Fact]
    public void RequestRestart_StartsTheNewInstanceBeforeAskingToQuit()
    {
        Lifecycle().RequestRestart();

        _calls.ShouldBe(["start", "restartRequested"]);
    }

    /// <summary>The caller (the IPC bridge) must hear of it, so the page can say the restart failed.</summary>
    [Fact]
    public void RequestRestart_NewInstanceCannotStart_ThrowsAndDoesNotAskToQuit()
    {
        var lifecycle = Lifecycle(() => throw new Win32Exception(2));

        Should.Throw<Win32Exception>(lifecycle.RequestRestart);

        _calls.ShouldBeEmpty();
        lifecycle.IsQuitting.ShouldBeFalse();
    }

    [Fact]
    public void RequestRestart_AfterAFailedStart_TriesAgain()
    {
        var attempts = 0;
        var lifecycle = Lifecycle(() =>
        {
            if (++attempts == 1)
                throw new Win32Exception(2);
        });

        Should.Throw<Win32Exception>(lifecycle.RequestRestart);
        lifecycle.RequestRestart();

        attempts.ShouldBe(2);
        _calls.ShouldBe(["restartRequested"]);
    }

    /// <summary>Two quick clicks must not start two new instances.</summary>
    [Fact]
    public void RequestRestart_Twice_StartsOneNewInstance()
    {
        var lifecycle = Lifecycle();

        lifecycle.RequestRestart();
        lifecycle.RequestRestart();

        _calls.ShouldBe(["start", "restartRequested"]);
    }

    [Fact]
    public void RequestRestart_WhileQuitting_DoesNothing()
    {
        var lifecycle = Lifecycle();
        lifecycle.MarkQuitting();

        lifecycle.RequestRestart();

        _calls.ShouldBeEmpty();
    }
}
