namespace Murmur.Core.Setup;

/// <summary>What the command line asked of this launch. Set by App before the window opens.</summary>
public sealed class LaunchOptions
{
    /// <summary>The page the main window should open on; handed out once by <see cref="SetupCoordinator.GetStateAsync"/>.</summary>
    public string? OpenPage { get; set; }
}
