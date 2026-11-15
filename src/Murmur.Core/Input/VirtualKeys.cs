namespace Murmur.Core.Input;

/// <summary>
/// The Windows virtual-key codes Murmur refers to by name. Public because the UI's input hook host needs them too.
/// </summary>
public static class VirtualKeys
{
    /// <summary>Shift, either side. Read from the key state; the low-level hook reports the side-specific codes.</summary>
    public const int Shift = 0x10;

    /// <summary>Ctrl, either side.</summary>
    public const int Ctrl = 0x11;

    /// <summary>Alt, either side (VK_MENU).</summary>
    public const int Alt = 0x12;

    public const int Escape = 0x1B;

    /// <summary>The Win keys have no generic code that covers both sides.</summary>
    public const int LeftWin = 0x5B;

    public const int RightWin = 0x5C;
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftCtrl = 0xA2;
    public const int RightCtrl = 0xA3;
    public const int LeftAlt = 0xA4;
    public const int RightAlt = 0xA5;

    /// <summary>
    /// Every modifier code: the side-specific ones the low-level hook reports, then the generic Shift, Ctrl and Alt in
    /// case they appear.
    /// </summary>
    public static ReadOnlySpan<int> Modifiers =>
        [LeftShift, RightShift, LeftCtrl, RightCtrl, LeftAlt, RightAlt, LeftWin, RightWin, Shift, Ctrl, Alt];

    /// <summary>
    /// One code per modifier key that reads as down when either side is down: the generic Shift, Ctrl and Alt, and both
    /// Win keys.
    /// </summary>
    public static ReadOnlySpan<int> EitherSideModifiers => [Shift, Ctrl, Alt, LeftWin, RightWin];

    /// <summary>True for the left/right-specific modifier codes (the ones a modifier-only hotkey can use).</summary>
    public static bool IsSideSpecificModifier(int vk) => vk is >= LeftShift and <= RightAlt or LeftWin or RightWin;
}
