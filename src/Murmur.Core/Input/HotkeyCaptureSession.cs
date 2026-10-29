using Murmur.Core.Models;

namespace Murmur.Core.Input;

/// <summary>
/// Records one new hotkey from raw key events:
/// <list type="bullet">
/// <item>A modifier pressed and released with no other key in between → that side-specific modifier alone.</item>
/// <item>A non-modifier key-down → that key plus the modifiers currently held, accepted only if
/// <see cref="HotkeyDefinition.IsValid"/>; otherwise it is ignored and the session keeps waiting.</item>
/// <item>Esc cancels (<see cref="Result"/> stays null).</item>
/// </list>
/// Injected input is ignored. Not thread-safe.
/// </summary>
public sealed class HotkeyCaptureSession
{
    private const int VkEscape = 0x1B;

    private readonly ModifierKeyTracker _modifiers = new();

    // The modifier that went down while no other modifier was held; 0 once any other key joins in.
    private int _soloModifier;

    public bool IsComplete { get; private set; }

    /// <summary>The captured hotkey; null while incomplete or when cancelled.</summary>
    public HotkeyDefinition? Result { get; private set; }

    public void OnKey(KeyInput input)
    {
        if (IsComplete || input.IsInjected)
            return;

        var vk = input.VirtualKey;

        if (vk == VkEscape)
        {
            if (input.IsDown)
                Complete(null);
            return;
        }

        if (ModifierKeyTracker.IsModifier(vk))
        {
            OnModifier(vk, input.IsDown);
            return;
        }

        if (!input.IsDown)
            return;

        _soloModifier = 0;
        var mods = _modifiers.Current();
        var candidate = new HotkeyDefinition(vk, mods, KeyNames.Describe(vk, mods));
        if (candidate.IsValid)
            Complete(candidate);
    }

    private void OnModifier(int vk, bool isDown)
    {
        if (isDown)
        {
            if (_modifiers.IsDown(vk))
                return; // auto-repeat

            _soloModifier = !_modifiers.AnyDown && ModifierKeyTracker.IsSideSpecific(vk) ? vk : 0;
            _modifiers.Set(vk, true);
            return;
        }

        _modifiers.Set(vk, false);
        if (vk == _soloModifier)
            Complete(new HotkeyDefinition(vk, HotkeyModifiers.None, KeyNames.For(vk)));
    }

    private void Complete(HotkeyDefinition? result)
    {
        IsComplete = true;
        Result = result;
    }
}
