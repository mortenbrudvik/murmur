using Murmur.Core.Interop;

namespace Murmur.Core.Input;

/// <summary>
/// <see cref="IKeyboardLayout"/> over <c>MapVirtualKeyEx</c>, using the layout of the foreground window's thread: while
/// a hotkey is captured that is Murmur's own window, and the hook thread's layout can be stale. It only reads a layout
/// the user already has. It never loads one, which would change their language list, and it leaves the dead-key state
/// alone, unlike <c>ToUnicodeEx</c>. Not <c>GetKeyNameText</c> either: that answers in the layout's own language.
/// </summary>
public sealed class Win32KeyboardLayout : IKeyboardLayout
{
    /// <summary><c>MapVirtualKeyEx</c> sets the top bit of its result for a dead key.</summary>
    private const uint DeadKeyBit = 0x80000000;

    private readonly Func<nint> _layoutHandle;

    public Win32KeyboardLayout() : this(ForegroundThreadLayout)
    {
    }

    /// <param name="layoutHandle">Gives the input locale identifier (HKL) to ask; 0 when there is none.</param>
    internal Win32KeyboardLayout(Func<nint> layoutHandle) => _layoutHandle = layoutHandle;

    public char? CharacterFor(int virtualKey)
    {
        var layout = _layoutHandle();
        if (layout == 0)
            return null;

        return ToCharacter(NativeMethods.MapVirtualKeyEx((uint)virtualKey, NativeMethods.MAPVK_VK_TO_CHAR, layout));
    }

    /// <summary>The character in a <c>MapVirtualKeyEx</c> result, without the dead-key bit; null when there is none.</summary>
    internal static char? ToCharacter(uint mapped)
    {
        var character = mapped & ~DeadKeyBit;
        return character is 0 or > char.MaxValue ? null : (char)character;
    }

    private static nint ForegroundThreadLayout()
    {
        var thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        return NativeMethods.GetKeyboardLayout(thread);
    }
}
