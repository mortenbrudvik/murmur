using Murmur.Core.Models;

namespace Murmur.Core.Input;

public enum HotkeySignal
{
    None,
    HotkeyDown,
    HotkeyUp,
    OtherInputWhileHeld,
    Escape,
}

/// <summary>One low-level keyboard event. <paramref name="IsInjected"/> is set for synthesized input (SendInput).</summary>
public readonly record struct KeyInput(int VirtualKey, bool IsDown, bool IsInjected);

/// <summary>What a key event means for dictation, and whether the hook should keep it from reaching other apps.</summary>
public readonly record struct MatchResult(HotkeySignal Signal, bool Swallow);

/// <summary>
/// Turns raw low-level key events into dictation signals for the configured hotkey.
/// <list type="bullet">
/// <item>Modifier-only hotkey (e.g. Right Ctrl): down → <see cref="HotkeySignal.HotkeyDown"/>, up →
/// <see cref="HotkeySignal.HotkeyUp"/>; never swallowed, so the key keeps working as a modifier.</item>
/// <item>Combo hotkey: the main key's down while exactly the configured modifiers are held →
/// <see cref="HotkeySignal.HotkeyDown"/>; its up → <see cref="HotkeySignal.HotkeyUp"/>; both swallowed.</item>
/// <item>Auto-repeat downs of the held hotkey key are dropped, so <see cref="HotkeySignal.HotkeyDown"/> fires only on
/// the first down after an up.</item>
/// <item>Another key-down while the hotkey is held → <see cref="HotkeySignal.OtherInputWhileHeld"/>, not swallowed.</item>
/// <item>Esc while <see cref="DictationActive"/> → <see cref="HotkeySignal.Escape"/>, swallowed down and up.</item>
/// <item>Injected input (such as Murmur's own Ctrl+V) is ignored entirely.</item>
/// </list>
/// Called from the keyboard hook on the UI thread; <see cref="Hotkey"/> and <see cref="DictationActive"/> may be set
/// from any thread.
/// </summary>
public sealed class HotkeyMatcher(HotkeyDefinition hotkey)
{
    private const int VkEscape = 0x1B;

    private readonly Lock _gate = new();
    private readonly ModifierKeyTracker _modifiers = new();
    private HotkeyDefinition _hotkey = hotkey;
    private bool _held;
    private bool _escapeHeld;
    private volatile bool _dictationActive;

    public HotkeyDefinition Hotkey
    {
        get
        {
            lock (_gate)
                return _hotkey;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                _hotkey = value;
                _held = false;
            }
        }
    }

    /// <summary>Set by the controller while a dictation session is in progress; enables Esc interception.</summary>
    public bool DictationActive
    {
        get => _dictationActive;
        set => _dictationActive = value;
    }

    public bool IsHotkeyHeld
    {
        get
        {
            lock (_gate)
                return _held;
        }
    }

    /// <summary>
    /// Forgets that the hotkey (and a swallowed Esc) is held, without touching the tracked modifiers. Called after a
    /// missed key release was recovered by polling, and before a hotkey capture starts, so the next press is not
    /// mistaken for auto-repeat.
    /// </summary>
    public void ResetHeld()
    {
        lock (_gate)
        {
            _held = false;
            _escapeHeld = false;
        }
    }

    public MatchResult OnKey(KeyInput input)
    {
        if (input.IsInjected)
            return default;

        lock (_gate)
        {
            var vk = input.VirtualKey;

            // A modifier that is already down and reported down again is auto-repeat, not new input.
            var modifierRepeat = input.IsDown && _modifiers.IsDown(vk);
            var result = Match(input, modifierRepeat);
            _modifiers.Set(vk, input.IsDown);
            return result;
        }
    }

    /// <summary>A mouse button went down or a wheel turned (injected mouse input is filtered by the hook).</summary>
    public MatchResult OnMouseButtonOrWheel()
    {
        lock (_gate)
            return _held ? new MatchResult(HotkeySignal.OtherInputWhileHeld, false) : default;
    }

    private MatchResult Match(KeyInput input, bool modifierRepeat)
    {
        var vk = input.VirtualKey;

        if (vk == VkEscape && TryMatchEscape(input.IsDown, out var escape))
            return escape;

        if (vk == _hotkey.VirtualKey)
            return _hotkey.IsModifierOnly ? MatchModifierOnly(input.IsDown) : MatchCombo(input.IsDown);

        if (input.IsDown && _held && !modifierRepeat)
            return new MatchResult(HotkeySignal.OtherInputWhileHeld, false);

        return default;
    }

    private bool TryMatchEscape(bool isDown, out MatchResult result)
    {
        if (isDown)
        {
            if (_dictationActive)
            {
                // Signal once per press; auto-repeats are swallowed silently.
                result = new MatchResult(_escapeHeld ? HotkeySignal.None : HotkeySignal.Escape, true);
                _escapeHeld = true;
                return true;
            }

            if (_escapeHeld)
            {
                // The session was cancelled by this press: keep swallowing its auto-repeats.
                result = new MatchResult(HotkeySignal.None, true);
                return true;
            }
        }
        else if (_escapeHeld)
        {
            _escapeHeld = false;
            result = new MatchResult(HotkeySignal.None, true);
            return true;
        }

        result = default;
        return false;
    }

    private MatchResult MatchModifierOnly(bool isDown)
    {
        if (isDown)
        {
            if (_held)
                return default;

            _held = true;
            return new MatchResult(HotkeySignal.HotkeyDown, false);
        }

        if (!_held)
            return default;

        _held = false;
        return new MatchResult(HotkeySignal.HotkeyUp, false);
    }

    private MatchResult MatchCombo(bool isDown)
    {
        if (isDown)
        {
            if (_held)
                return new MatchResult(HotkeySignal.None, true);

            // The main key may itself be a modifier key; it must not count towards its own combo.
            if (_modifiers.Current(exceptVk: _hotkey.VirtualKey) != _hotkey.Modifiers)
                return default;

            _held = true;
            return new MatchResult(HotkeySignal.HotkeyDown, true);
        }

        if (!_held)
            return default;

        _held = false;
        return new MatchResult(HotkeySignal.HotkeyUp, true);
    }
}

/// <summary>Which modifier keys are down, tracked per physical key (left and right separately) from key events.</summary>
internal sealed class ModifierKeyTracker
{
    // Side-specific keys as reported by the low-level hook, plus the generic Shift/Ctrl/Alt codes in case they appear.
    private static readonly int[] Keys = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C, 0x10, 0x11, 0x12];

    private uint _down;

    public bool AnyDown => _down != 0;

    public static bool IsModifier(int vk) => Slot(vk) >= 0;

    /// <summary>True for the left/right-specific modifier codes (the ones a modifier-only hotkey can use).</summary>
    public static bool IsSideSpecific(int vk) => vk is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    public static HotkeyModifiers FlagOf(int vk) => vk switch
    {
        0xA0 or 0xA1 or 0x10 => HotkeyModifiers.Shift,
        0xA2 or 0xA3 or 0x11 => HotkeyModifiers.Ctrl,
        0xA4 or 0xA5 or 0x12 => HotkeyModifiers.Alt,
        0x5B or 0x5C => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    public bool IsDown(int vk)
    {
        var slot = Slot(vk);
        return slot >= 0 && (_down & (1u << slot)) != 0;
    }

    /// <summary>Records a key event; non-modifier keys are ignored.</summary>
    public void Set(int vk, bool down)
    {
        var slot = Slot(vk);
        if (slot < 0)
            return;

        if (down)
            _down |= 1u << slot;
        else
            _down &= ~(1u << slot);
    }

    /// <summary>The side-agnostic modifiers currently held, optionally leaving one key out.</summary>
    public HotkeyModifiers Current(int exceptVk = 0)
    {
        var mods = HotkeyModifiers.None;
        foreach (var vk in Keys)
        {
            if (vk != exceptVk && IsDown(vk))
                mods |= FlagOf(vk);
        }

        return mods;
    }

    private static int Slot(int vk) => Array.IndexOf(Keys, vk);
}
