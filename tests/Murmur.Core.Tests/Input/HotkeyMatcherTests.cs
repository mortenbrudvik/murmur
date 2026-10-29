using Murmur.Core.Input;
using Murmur.Core.Models;
using Shouldly;

namespace Murmur.Core.Tests.Input;

public sealed class HotkeyMatcherTests
{
    private const int RightCtrl = 0xA3;
    private const int LeftCtrl = 0xA2;
    private const int RightShift = 0xA1;
    private const int LeftShift = 0xA0;
    private const int LeftAlt = 0xA4;
    private const int LeftWin = 0x5B;
    private const int Space = 0x20;
    private const int Escape = 0x1B;
    private const int KeyC = 0x43;
    private const int KeyV = 0x56;

    private static readonly HotkeyDefinition CtrlShiftSpace =
        new(Space, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space");

    private static KeyInput Down(int vk) => new(vk, IsDown: true, IsInjected: false);

    private static KeyInput Up(int vk) => new(vk, IsDown: false, IsInjected: false);

    private static MatchResult Result(HotkeySignal signal, bool swallow) => new(signal, swallow);

    private static HotkeyMatcher NewMatcher() => new(HotkeyDefinition.Default);

    [Fact]
    public void ModifierOnly_DownUp()
    {
        var matcher = NewMatcher();

        matcher.OnKey(Down(RightCtrl)).ShouldBe(Result(HotkeySignal.HotkeyDown, false));
        matcher.IsHotkeyHeld.ShouldBeTrue();
        matcher.OnKey(Up(RightCtrl)).ShouldBe(Result(HotkeySignal.HotkeyUp, false));
        matcher.IsHotkeyHeld.ShouldBeFalse();
    }

    [Fact]
    public void ModifierOnly_AutoRepeat_SingleDown()
    {
        var matcher = NewMatcher();

        var signals = new[] { Down(RightCtrl), Down(RightCtrl), Down(RightCtrl) }
            .Select(k => matcher.OnKey(k).Signal)
            .ToArray();

        signals.ShouldBe([HotkeySignal.HotkeyDown, HotkeySignal.None, HotkeySignal.None]);
    }

    [Fact]
    public void ModifierOnly_SecondPressAfterRelease_SignalsDownAgain()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl));
        matcher.OnKey(Up(RightCtrl));

        matcher.OnKey(Down(RightCtrl)).ShouldBe(Result(HotkeySignal.HotkeyDown, false));
    }

    [Fact]
    public void ModifierOnly_OtherKeyWhileHeld()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl));

        matcher.OnKey(Down(KeyC)).ShouldBe(Result(HotkeySignal.OtherInputWhileHeld, false));
    }

    [Fact]
    public void ModifierOnly_OtherKeyUpWhileHeld_None()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl));

        matcher.OnKey(Up(KeyC)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void ModifierOnly_OtherKeyWhenNotHeld_None()
    {
        var matcher = NewMatcher();

        matcher.OnKey(Down(KeyC)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void ModifierOnly_UpWithoutDown_None()
    {
        var matcher = NewMatcher();

        matcher.OnKey(Up(RightCtrl)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void LeftCtrl_DoesNotTriggerRightCtrl()
    {
        var matcher = NewMatcher();

        matcher.OnKey(Down(LeftCtrl)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.IsHotkeyHeld.ShouldBeFalse();
    }

    [Fact]
    public void InjectedKeys_Ignored()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl));

        matcher.OnKey(new KeyInput(KeyV, true, IsInjected: true)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void InjectedHotkey_Ignored()
    {
        var matcher = NewMatcher();

        matcher.OnKey(new KeyInput(RightCtrl, true, IsInjected: true)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.IsHotkeyHeld.ShouldBeFalse();
    }

    [Fact]
    public void Combo_CtrlShiftSpace_Swallowed()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);

        matcher.OnKey(Down(LeftCtrl)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.OnKey(Down(LeftShift)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.HotkeyDown, true));
        matcher.IsHotkeyHeld.ShouldBeTrue();
        matcher.OnKey(Up(Space)).ShouldBe(Result(HotkeySignal.HotkeyUp, true));
        matcher.IsHotkeyHeld.ShouldBeFalse();
    }

    [Fact]
    public void Combo_MainKeyAlone_PassesThrough()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.OnKey(Up(Space)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void Combo_RightSideModifiers_Match()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(RightCtrl));
        matcher.OnKey(Down(RightShift));

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.HotkeyDown, true));
    }

    [Fact]
    public void Combo_ExtraModifier_PassesThrough()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));
        matcher.OnKey(Down(LeftAlt));

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void Combo_ReleasedModifier_NoLongerCounts()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));
        matcher.OnKey(Up(LeftShift));

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void Combo_BothCtrlKeysDown_ReleasingOneKeepsCtrl()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(RightCtrl));
        matcher.OnKey(Up(RightCtrl));
        matcher.OnKey(Down(LeftShift));

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.HotkeyDown, true));
    }

    [Fact]
    public void Combo_GenericModifierCodes_Count()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(0x11));
        matcher.OnKey(Down(0x10));

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.HotkeyDown, true));
    }

    [Fact]
    public void Combo_AutoRepeat_SingleDownAndRepeatsSwallowed()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.HotkeyDown, true));
        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.None, true));
        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.None, true));
    }

    [Fact]
    public void Combo_ModifiersReleasedBeforeMainKey_StillSignalsUp()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));
        matcher.OnKey(Down(Space));
        matcher.OnKey(Up(LeftShift)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.OnKey(Up(LeftCtrl)).ShouldBe(Result(HotkeySignal.None, false));

        matcher.OnKey(Up(Space)).ShouldBe(Result(HotkeySignal.HotkeyUp, true));
    }

    [Fact]
    public void Combo_OtherKeyWhileHeld_NotSwallowed()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));
        matcher.OnKey(Down(Space));

        matcher.OnKey(Down(KeyC)).ShouldBe(Result(HotkeySignal.OtherInputWhileHeld, false));
    }

    [Fact]
    public void Combo_HeldModifierRepeat_IsNotOtherInput()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));
        matcher.OnKey(Down(Space));

        matcher.OnKey(Down(LeftShift)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void Combo_NewModifierWhileHeld_IsOtherInput()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));
        matcher.OnKey(Down(Space));

        matcher.OnKey(Down(LeftWin)).ShouldBe(Result(HotkeySignal.OtherInputWhileHeld, false));
    }

    [Fact]
    public void Combo_ModifierAsMainKey_OwnModifierDoesNotCount()
    {
        var matcher = new HotkeyMatcher(new HotkeyDefinition(RightShift, HotkeyModifiers.Ctrl, "Ctrl+Right Shift"));
        matcher.OnKey(Down(LeftCtrl));

        matcher.OnKey(Down(RightShift)).ShouldBe(Result(HotkeySignal.HotkeyDown, true));
        matcher.OnKey(Up(RightShift)).ShouldBe(Result(HotkeySignal.HotkeyUp, true));
    }

    [Fact]
    public void Escape_WhenActive_SwallowedBothWays()
    {
        var matcher = NewMatcher();
        matcher.DictationActive = true;

        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.Escape, true));
        matcher.OnKey(Up(Escape)).ShouldBe(Result(HotkeySignal.None, true));
    }

    [Fact]
    public void Escape_WhenInactive_PassesThrough()
    {
        var matcher = NewMatcher();

        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.OnKey(Up(Escape)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void Escape_DeactivatedBeforeRelease_RepeatsAndUpStillSwallowed()
    {
        var matcher = NewMatcher();
        matcher.DictationActive = true;
        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.Escape, true));

        // The controller cancels the session and clears DictationActive while Esc is still down.
        matcher.DictationActive = false;

        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.None, true));
        matcher.OnKey(Up(Escape)).ShouldBe(Result(HotkeySignal.None, true));
        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void Escape_AutoRepeatWhileActive_SignalsOnce()
    {
        var matcher = NewMatcher();
        matcher.DictationActive = true;

        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.Escape, true));
        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.None, true));
    }

    [Fact]
    public void Escape_WhileHotkeyHeldAndActive_IsEscapeNotOtherInput()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl));
        matcher.DictationActive = true;

        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.Escape, true));
    }

    [Fact]
    public void Escape_InjectedWhileActive_Ignored()
    {
        var matcher = NewMatcher();
        matcher.DictationActive = true;

        matcher.OnKey(new KeyInput(Escape, true, IsInjected: true)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void Mouse_WhileHeld_OtherInput()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl));

        matcher.OnMouseButtonOrWheel().ShouldBe(Result(HotkeySignal.OtherInputWhileHeld, false));
    }

    [Fact]
    public void Mouse_NotHeld_None()
    {
        var matcher = NewMatcher();

        matcher.OnMouseButtonOrWheel().ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void HotkeyChangedWhileHeld_ResetsHeldState()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl));

        matcher.Hotkey = CtrlShiftSpace;

        matcher.Hotkey.ShouldBe(CtrlShiftSpace);
        matcher.IsHotkeyHeld.ShouldBeFalse();
        matcher.OnMouseButtonOrWheel().ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void ResetHeld_AfterHotkeyDown_NextDownSignalsAgain()
    {
        var matcher = NewMatcher();
        matcher.OnKey(Down(RightCtrl)).Signal.ShouldBe(HotkeySignal.HotkeyDown);

        matcher.ResetHeld();

        matcher.IsHotkeyHeld.ShouldBeFalse();
        matcher.OnKey(Down(RightCtrl)).ShouldBe(Result(HotkeySignal.HotkeyDown, false));
    }

    [Fact]
    public void ResetHeld_AfterSwallowedEscape_NextEscapePassesThrough()
    {
        var matcher = NewMatcher();
        matcher.DictationActive = true;
        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.Escape, true));
        matcher.DictationActive = false;

        matcher.ResetHeld();

        matcher.OnKey(Down(Escape)).ShouldBe(Result(HotkeySignal.None, false));
    }

    [Fact]
    public void ResetHeld_KeepsTrackedModifiers()
    {
        var matcher = new HotkeyMatcher(CtrlShiftSpace);
        matcher.OnKey(Down(LeftCtrl));
        matcher.OnKey(Down(LeftShift));

        matcher.ResetHeld();

        matcher.OnKey(Down(Space)).ShouldBe(Result(HotkeySignal.HotkeyDown, true));
    }

    [Fact]
    public void HotkeyChanged_NewHotkeyMatches()
    {
        var matcher = NewMatcher();
        matcher.Hotkey = new HotkeyDefinition(LeftAlt, HotkeyModifiers.None, "Left Alt");

        matcher.OnKey(Down(RightCtrl)).ShouldBe(Result(HotkeySignal.None, false));
        matcher.OnKey(Down(LeftAlt)).ShouldBe(Result(HotkeySignal.HotkeyDown, false));
    }
}
