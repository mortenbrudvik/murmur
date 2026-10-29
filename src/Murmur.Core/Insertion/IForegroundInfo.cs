namespace Murmur.Core.Insertion;

/// <summary>The window that will receive the paste.</summary>
/// <param name="Window">The foreground window handle (0 if there is none).</param>
/// <param name="AppName">A display name for the owning app, for history.</param>
/// <param name="IsElevated">
/// The owning process runs elevated (or Murmur cannot tell because it is denied access). Windows blocks Murmur's
/// injected Ctrl+V into such a window.
/// </param>
public sealed record ForegroundTarget(nint Window, string AppName, bool IsElevated);

/// <summary>Identifies the window that currently has the keyboard focus.</summary>
public interface IForegroundInfo
{
    ForegroundTarget Capture();
}
