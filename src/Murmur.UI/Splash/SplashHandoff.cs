namespace Murmur.UI.Splash;

/// <summary>The rule for the moment the splash hands over to the main window.</summary>
internal static class SplashHandoff
{
    /// <summary>
    /// Whether the main window should take the focus when the splash closes: only if Murmur still has the foreground.
    /// A user who has gone back to another app in the meantime is left there, so a dictation already under way is not
    /// pasted into Murmur.
    /// </summary>
    /// <param name="foregroundProcessId">The process that owns the foreground window; 0 when none does.</param>
    internal static bool ShouldActivate(uint foregroundProcessId, uint ownProcessId) =>
        foregroundProcessId != 0 && foregroundProcessId == ownProcessId;
}
