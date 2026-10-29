using System.ComponentModel;
using System.Runtime.InteropServices;
using Murmur.Core.Interop;

namespace Murmur.Core.Insertion;

/// <summary>
/// <see cref="IKeyboardSender"/> over <c>SendInput</c>. Each key sequence goes in as a single call, so no physical
/// keystroke can land in the middle of it. The events carry the injected flag, which Murmur's own hook ignores.
/// </summary>
public sealed class Win32KeyboardSender : IKeyboardSender
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    /// <summary>Unassigned virtual-key code, so pressing it does nothing in any app.</summary>
    private const ushort VK_MASK = 0xE8;

    public void SendCtrlV()
    {
        Span<NativeMethods.INPUT> inputs =
        [
            Key(VK_CONTROL, keyUp: false),
            Key(VK_V, keyUp: false),
            Key(VK_V, keyUp: true),
            Key(VK_CONTROL, keyUp: true),
        ];

        var sent = Send(inputs);
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"SendInput inserted {sent} of {inputs.Length} key events.");
    }

    public void SendMaskKey()
    {
        Span<NativeMethods.INPUT> inputs = [Key(VK_MASK, keyUp: false), Key(VK_MASK, keyUp: true)];
        Send(inputs);
    }

    private static unsafe uint Send(Span<NativeMethods.INPUT> inputs)
    {
        fixed (NativeMethods.INPUT* pointer = inputs)
            return NativeMethods.SendInput((uint)inputs.Length, pointer, sizeof(NativeMethods.INPUT));
    }

    private static NativeMethods.INPUT Key(ushort virtualKey, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                // Apps that read scan codes rather than virtual keys still see the right key.
                wScan = (ushort)NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MAPVK_VK_TO_VSC),
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
            },
        },
    };
}
