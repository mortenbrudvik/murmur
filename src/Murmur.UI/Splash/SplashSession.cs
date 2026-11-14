using Murmur.Core.Setup;

namespace Murmur.UI.Splash;

/// <summary>
/// One showing of the splash, from the moment it is up to its hand-over to the main window: what a request for the
/// window means meanwhile, who gets the focus at the end, and that the splash is closed whatever happens. It knows the
/// windows only through the delegates it is given, so its rules can be tested without any. Call on the UI thread,
/// except <see cref="MarkUiReady"/>, which only passes on to the gate.
/// </summary>
internal sealed class SplashSession(
    SplashGate gate,
    Func<bool> mainWindowIsShown,
    Action<bool> revealMainWindow,
    Action closeSplash,
    Func<uint> foregroundProcessId,
    uint ownProcessId)
{
    private bool _windowRequested;

    /// <summary>The splash has handed over or been abandoned; nothing here does anything any more.</summary>
    public bool IsOver { get; private set; }

    /// <summary>Start-up is done and the splash's animation has begun: the gate's clock starts.</summary>
    public void Begin() => gate.Start();

    /// <summary>The web UI has drawn its first page. Safe from any thread.</summary>
    public void MarkUiReady() => gate.MarkReady();

    /// <summary>A click or a key on the splash, or the still unseen main window was activated from the taskbar.</summary>
    public void Skip()
    {
        if (!IsOver)
            gate.Skip();
    }

    /// <summary>
    /// The main window was asked for while the splash is up: the tray icon, or a second launch. It cuts the splash
    /// short, and the window then comes to the front whatever app has the focus, because the user has just asked for it.
    /// </summary>
    public void RequestWindow()
    {
        if (IsOver)
            return;

        _windowRequested = true;
        gate.Skip();
    }

    /// <summary>
    /// The gate has closed: the main window is made visible and the splash is closed. The window takes the focus if
    /// it was asked for, and otherwise only if Murmur still has the foreground (<see cref="SplashHandoff"/>). If
    /// start-up has not shown the main window yet (an early skip), there is nothing to reveal: start-up finds the
    /// session over and shows the window itself. The splash is closed even if revealing the window throws: it is
    /// topmost, and nothing else would ever close it.
    /// </summary>
    public void HandOver()
    {
        if (IsOver)
            return;

        IsOver = true;
        gate.Dispose();
        try
        {
            if (mainWindowIsShown())
                revealMainWindow(_windowRequested || SplashHandoff.ShouldActivate(foregroundProcessId(), ownProcessId));
        }
        finally
        {
            closeSplash();
        }
    }

    /// <summary>Quit, or a start-up that failed: the splash goes at once, with no hand-over.</summary>
    public void Abandon()
    {
        if (IsOver)
            return;

        IsOver = true;
        gate.Dispose();
        closeSplash();
    }
}
