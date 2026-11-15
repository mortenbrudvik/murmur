using Murmur.Core.Interop;

namespace Murmur.Core.Input;

/// <summary>
/// <see cref="IKeyStateProbe"/> over <c>GetAsyncKeyState</c>. Note that a key event swallowed by a low-level hook never
/// updates this state, so a swallowed key reads as up.
/// </summary>
public sealed class Win32KeyStateProbe : IKeyStateProbe
{
    public bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public bool IsAnyModifierDown()
    {
        foreach (var vk in VirtualKeys.EitherSideModifiers)
        {
            if (IsKeyDown(vk))
                return true;
        }

        return false;
    }
}
