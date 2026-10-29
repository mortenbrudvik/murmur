namespace Murmur.Core.Insertion;

/// <summary>Injects keystrokes into the focused window.</summary>
public interface IKeyboardSender
{
    /// <summary>Sends Ctrl+V.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The input could not be injected.</exception>
    void SendCtrlV();

    /// <summary>
    /// Sends a press of an unassigned key (VK 0xE8). Call it right after a hotkey-down when the hotkey is Alt or Win
    /// held alone: releasing Alt or Win with no other key pressed in between activates the app's menu bar or opens
    /// the Start menu, and this in-between key prevents that (AutoHotkey's "menu mask" technique). Best effort;
    /// never throws, so it is safe to call from the keyboard hook.
    /// </summary>
    void SendMaskKey();
}
