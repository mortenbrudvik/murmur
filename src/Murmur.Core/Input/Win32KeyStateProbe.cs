using Murmur.Core.Interop;

namespace Murmur.Core.Input;

/// <summary>
/// <see cref="IKeyStateProbe"/> over <c>GetAsyncKeyState</c>. Note that a key event swallowed by a low-level hook never
/// updates this state, so a swallowed key reads as up.
/// </summary>
public sealed class Win32KeyStateProbe : IKeyStateProbe
{
    // Generic Shift, Ctrl and Alt cover both sides; the Win keys have no generic code.
    private static readonly int[] ModifierKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C];

    public bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public bool IsAnyModifierDown()
    {
        foreach (var vk in ModifierKeys)
        {
            if (IsKeyDown(vk))
                return true;
        }

        return false;
    }
}
