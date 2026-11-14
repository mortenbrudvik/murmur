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
/// <see cref="HotkeySignal.HotkeyDown"/>; its up → <see cref="HotkeySignal.HotkeyUp"/>; both swallowed, and so is every
/// down of the main key in between, also once its modifiers are up.</item>
/// <item>Auto-repeat downs of the held hotkey key are dropped, so <see cref="HotkeySignal.HotkeyDown"/> fires only on
/// the first down after an up. The exception is a combo whose modifiers were all released while its main key was held:
/// should the key-up never come, the combo pressed again is a new press.</item>
/// <item>Another key-down while the hotkey is held → <see cref="HotkeySignal.OtherInputWhileHeld"/>, not swallowed.</item>
/// <item>Esc while <see cref="DictationActive"/> → <see cref="HotkeySignal.Escape"/>, swallowed down and up.</item>
/// <item>Injected input (such as Murmur's own Ctrl+V) is ignored entirely.</item>
/// <item>A modifier whose key-up the hook never saw (Windows took the keyboard away: Win+L, a UAC prompt) is forgotten
/// when a combo's main key next goes down, by reading the real key state, so it cannot block or fake the combo.</item>
/// </list>
/// Called from the keyboard hook's thread, while the controller reads and resets it from the UI thread: every member
/// is thread-safe (state under a lock, <see cref="DictationActive"/> volatile).
/// </summary>
public sealed class HotkeyMatcher(HotkeyDefinition hotkey, IKeyStateProbe keys)
{
    private readonly IKeyStateProbe _keys = keys;

    private readonly Lock _gate = new();
    private readonly ModifierKeyTracker _modifiers = new();
    private HotkeyDefinition _hotkey = hotkey;
    private bool _held;

    // While a combo with modifiers is held: all of its modifiers were seen released since it went down. The main key is
    // still swallowed (it may be down and auto-repeating), but the combo pressed again is a new press.
    private bool _modifiersReleased;

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
                _modifiersReleased = false;
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
    /// lone modifier's missed release was recovered by polling, when a hold runs into its limit, and before a hotkey
    /// capture starts, so the next press is not mistaken for auto-repeat. Not for a combo whose modifiers were let go
    /// first: its main key may still be down, and <see cref="NoteModifiersReleased"/> keeps it swallowed.
    /// </summary>
    public void ResetHeld()
    {
        lock (_gate)
        {
            _held = false;
            _modifiersReleased = false;
            _escapeHeld = false;
        }
    }

    /// <summary>
    /// The controller read all of a held combo's modifiers as up, though the hook may have missed their key-ups. Like
    /// seeing them released: the main key stays swallowed until its key-up, but the combo pressed again is a new press.
    /// Does nothing unless a combo with modifiers is held.
    /// </summary>
    public void NoteModifiersReleased()
    {
        lock (_gate)
        {
            if (_held && HasModifiersToRelease(_hotkey))
                _modifiersReleased = true;
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

            // Modifier events are never swallowed, so the hook sees a held combo's modifiers go up even when the main key
            // is still down.
            if (_held && !input.IsDown && ModifierKeyTracker.IsModifier(vk) && HasModifiersToRelease(_hotkey)
                && (_modifiers.Current(exceptVk: _hotkey.VirtualKey) & _hotkey.Modifiers) == HotkeyModifiers.None)
            {
                _modifiersReleased = true;
            }

            return result;
        }
    }

    /// <summary>A mouse button went down or a wheel turned (injected mouse input is filtered by the hook).</summary>
    public MatchResult OnMouseButtonOrWheel()
    {
        lock (_gate)
            return _held ? new MatchResult(HotkeySignal.OtherInputWhileHeld, false) : default;
    }

    /// <summary>A combo with modifiers, which can be released before its main key; not a lone modifier or an F-key.</summary>
    private static bool HasModifiersToRelease(HotkeyDefinition hotkey) =>
        !hotkey.IsModifierOnly && hotkey.Modifiers != HotkeyModifiers.None;

    private MatchResult Match(KeyInput input, bool modifierRepeat)
    {
        var vk = input.VirtualKey;

        if (vk == VirtualKeys.Escape && TryMatchEscape(input.IsDown, out var escape))
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
            // Auto-repeat of the held key.
            if (_held && !_modifiersReleased)
                return new MatchResult(HotkeySignal.None, true);

            // A modifier's key-up never reaches the hook when Windows takes the keyboard away while it is held: Win+L,
            // Ctrl+Alt+Del, a UAC prompt, an elevated window coming to the front. The modifier would then count as
            // held for good: a combo would stop matching until that key was pressed again, or its main key alone
            // would start a dictation.
            _modifiers.ForgetReleased(_keys, exceptVk: _hotkey.VirtualKey);

            // The main key may itself be a modifier key; it must not count towards its own combo.
            if (_modifiers.Current(exceptVk: _hotkey.VirtualKey) != _hotkey.Modifiers)
            {
                // Its modifiers were let go first and the key is still down: its repeats stay swallowed until its key-up.
                return _held ? new MatchResult(HotkeySignal.None, true) : default;
            }

            // A fresh press, or the combo pressed again after its modifiers were released while the key-up was missed.
            _held = true;
            _modifiersReleased = false;
            return new MatchResult(HotkeySignal.HotkeyDown, true);
        }

        if (!_held)
            return default;

        _held = false;
        _modifiersReleased = false;
        return new MatchResult(HotkeySignal.HotkeyUp, true);
    }
}

/// <summary>Which modifier keys are down, tracked per physical key (left and right separately) from key events.</summary>
internal sealed class ModifierKeyTracker
{
    private uint _down;

    public bool AnyDown => _down != 0;

    public static bool IsModifier(int vk) => Slot(vk) >= 0;

    public static HotkeyModifiers FlagOf(int vk) => vk switch
    {
        VirtualKeys.LeftShift or VirtualKeys.RightShift or VirtualKeys.Shift => HotkeyModifiers.Shift,
        VirtualKeys.LeftCtrl or VirtualKeys.RightCtrl or VirtualKeys.Ctrl => HotkeyModifiers.Ctrl,
        VirtualKeys.LeftAlt or VirtualKeys.RightAlt or VirtualKeys.Alt => HotkeyModifiers.Alt,
        VirtualKeys.LeftWin or VirtualKeys.RightWin => HotkeyModifiers.Win,
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

    /// <summary>
    /// Forgets every modifier recorded as down that <paramref name="keys"/> reads as up: its key-up was missed. Only
    /// ever forgets. A key the events never showed is not added, because the key state also holds keys the hook leaves
    /// out on purpose (the fake Left Ctrl that comes with AltGr). <paramref name="exceptVk"/> is left alone: a combo's
    /// main key is swallowed, so it reads as up while it is held.
    /// </summary>
    public void ForgetReleased(IKeyStateProbe keys, int exceptVk)
    {
        if (_down == 0)
            return;

        foreach (var vk in VirtualKeys.Modifiers)
        {
            if (vk != exceptVk && IsDown(vk) && !keys.IsKeyDown(vk))
                Set(vk, down: false);
        }
    }

    /// <summary>The side-agnostic modifiers currently held, optionally leaving one key out.</summary>
    public HotkeyModifiers Current(int exceptVk = 0)
    {
        var mods = HotkeyModifiers.None;
        foreach (var vk in VirtualKeys.Modifiers)
        {
            if (vk != exceptVk && IsDown(vk))
                mods |= FlagOf(vk);
        }

        return mods;
    }

    /// <summary>The key's bit: its position in <see cref="VirtualKeys.Modifiers"/>, or -1 for a key that is no modifier.</summary>
    private static int Slot(int vk) => VirtualKeys.Modifiers.IndexOf(vk);
}
