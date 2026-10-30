using MediatR;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Audio;
using Murmur.Core.Features.Settings;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Murmur.Core.Tests.Features;

public sealed class SettingsHandlerTests
{
    private const int RightCtrl = 0xA3;
    private const int KeyA = 0x41;

    private static readonly HotkeyDefinition Previous = new(0x7C, HotkeyModifiers.None, "F13");
    private static readonly HotkeyDefinition CtrlShiftSpace = new(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space");

    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IStartupRegistration _startup = Substitute.For<IStartupRegistration>();
    private readonly IAudioRecorder _recorder = Substitute.For<IAudioRecorder>();
    private readonly FakeTimeProvider _time = new();

    // What the settings substitute holds, and what the last Update call stored.
    private MurmurSettings _stored = new(Previous, "mic-1", SoundCues: false, StartWithWindows: true, KeepAudio: true);
    private MurmurSettings? _saved;

    public SettingsHandlerTests()
    {
        _settings.Load().Returns(_ => _stored);
        _settings.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>())
            .Returns(ci => _saved = ci.Arg<Func<MurmurSettings, MurmurSettings>>()(_stored));
    }

    private static UpdateSettingsRequest Update(
        HotkeyDefinition? hotkey = null, string? microphoneId = null, bool soundCues = true,
        bool startWithWindows = false, bool keepAudio = false) =>
        new(hotkey ?? CtrlShiftSpace, microphoneId, soundCues, startWithWindows, keepAudio);

    private UpdateSettingsHandler UpdateHandler() => new(_settings, _mediator);

    [Fact]
    public async Task GetSettings_ReturnsTheStoredSettings()
    {
        var stored = _settings.Load();

        var result = await new GetSettingsHandler(_settings).Handle(new GetSettingsRequest(), CancellationToken.None);

        result.ShouldBe(stored);
    }

    [Fact]
    public async Task UpdateSettings_SavesAndReturnsTheNewSettings()
    {
        var result = await UpdateHandler().Handle(
            Update(microphoneId: "mic-2", soundCues: true, startWithWindows: false, keepAudio: false),
            CancellationToken.None);

        var expected = new MurmurSettings(CtrlShiftSpace, "mic-2", SoundCues: true, StartWithWindows: false, KeepAudio: false);
        result.ShouldBe(expected);
        _saved.ShouldBe(expected);
    }

    [Fact]
    public async Task UpdateSettings_InvalidHotkey_KeepsPrevious()
    {
        // A plain letter with no modifier would swallow typing.
        var result = await UpdateHandler().Handle(
            Update(hotkey: new HotkeyDefinition(KeyA, HotkeyModifiers.None, "A"), soundCues: true),
            CancellationToken.None);

        result.Hotkey.ShouldBe(Previous);
        var saved = _saved.ShouldNotBeNull();
        saved.Hotkey.ShouldBe(Previous);
        saved.SoundCues.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateSettings_MissingHotkey_KeepsPrevious()
    {
        var request = new UpdateSettingsRequest(null!, null, true, false, false);

        var result = await UpdateHandler().Handle(request, CancellationToken.None);

        result.Hotkey.ShouldBe(Previous);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task UpdateSettings_BlankMicrophone_MeansTheWindowsDefault(string? microphoneId)
    {
        var result = await UpdateHandler().Handle(Update(microphoneId: microphoneId), CancellationToken.None);

        result.MicrophoneId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_KeepsSetupAndModelFields()
    {
        _stored = new MurmurSettings(
            Previous, "mic-1", SoundCues: true, StartWithWindows: false, KeepAudio: false,
            SpeechModel: SpeechModelId.SmallEn, SetupStep: SetupStep.Microphone, SetupCompleted: true, CudaTipDismissed: true);

        var result = await UpdateHandler().Handle(Update(hotkey: Previous, microphoneId: "mic-1", soundCues: false), CancellationToken.None);

        var saved = _saved.ShouldNotBeNull();
        saved.SoundCues.ShouldBeFalse();
        saved.SpeechModel.ShouldBe(SpeechModelId.SmallEn);
        saved.SetupStep.ShouldBe(SetupStep.Microphone);
        saved.SetupCompleted.ShouldBeTrue();
        saved.CudaTipDismissed.ShouldBeTrue();
        result.ShouldBe(saved);
    }

    [Fact]
    public async Task UpdateSettings_PublishesSettingsUpdated()
    {
        var result = await UpdateHandler().Handle(Update(), CancellationToken.None);

        await _mediator.Received(1).Publish(
            Arg.Is<SettingsUpdatedNotification>(n => n.Settings == result),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateSettings_SaveFails_DoesNotPublish()
    {
        _settings.When(s => s.Update(Arg.Any<Func<MurmurSettings, MurmurSettings>>())).Throw(new IOException("disk full"));

        await Should.ThrowAsync<IOException>(() => UpdateHandler().Handle(Update(), CancellationToken.None));

        await _mediator.DidNotReceiveWithAnyArgs().Publish(default(SettingsUpdatedNotification)!, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsUpdatedHandler_AppliesHotkeyAndStartup()
    {
        var matcher = new HotkeyMatcher(Previous);
        var handler = new SettingsUpdatedHandler(matcher, _startup);
        var settings = new MurmurSettings(CtrlShiftSpace, StartWithWindows: false);

        await handler.Handle(new SettingsUpdatedNotification(settings), CancellationToken.None);

        matcher.Hotkey.ShouldBe(CtrlShiftSpace);
        _startup.Received(1).Apply(false);
    }

    [Fact]
    public async Task SettingsUpdatedHandler_StartupRegistrationFails_StillAppliesHotkeyAndDoesNotThrow()
    {
        var matcher = new HotkeyMatcher(Previous);
        _startup.When(s => s.Apply(Arg.Any<bool>())).Throw(new UnauthorizedAccessException("blocked by policy"));
        var handler = new SettingsUpdatedHandler(matcher, _startup);

        await handler.Handle(new SettingsUpdatedNotification(new MurmurSettings(CtrlShiftSpace, StartWithWindows: true)), CancellationToken.None);

        matcher.Hotkey.ShouldBe(CtrlShiftSpace);
        _startup.Received(1).Apply(true);
    }

    [Fact]
    public async Task CaptureHotkey_ReturnsCapturedDefinition()
    {
        var coordinator = new HotkeyCaptureCoordinator(_time);
        var handler = new CaptureHotkeyHandler(new HotkeyMatcher(Previous), coordinator);

        // Handle runs synchronously up to its first real wait, so the capture is already active here.
        var pending = handler.Handle(new CaptureHotkeyRequest(), CancellationToken.None);
        coordinator.TryHandle(new KeyInput(RightCtrl, IsDown: true, IsInjected: false)).ShouldBeTrue();
        coordinator.TryHandle(new KeyInput(RightCtrl, IsDown: false, IsInjected: false)).ShouldBeTrue();

        var result = await pending;

        result.ShouldBe(new HotkeyDefinition(RightCtrl, HotkeyModifiers.None, "Right Ctrl"));
    }

    [Fact]
    public async Task CaptureHotkey_ForgetsAHotkeyPressStillHeld_BeforeCapturing()
    {
        // The user clicked a button, so a held hotkey is stale: a lost key-up would otherwise make the next press look like auto-repeat.
        var matcher = new HotkeyMatcher(HotkeyDefinition.Default);
        matcher.OnKey(new KeyInput(RightCtrl, IsDown: true, IsInjected: false));
        matcher.IsHotkeyHeld.ShouldBeTrue();
        var handler = new CaptureHotkeyHandler(matcher, new HotkeyCaptureCoordinator(_time));

        var pending = handler.Handle(new CaptureHotkeyRequest(), CancellationToken.None);

        matcher.IsHotkeyHeld.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(10));
        await pending;
    }

    [Fact]
    public async Task CaptureHotkey_NothingPressedWithinTenSeconds_ReturnsNull()
    {
        var handler = new CaptureHotkeyHandler(new HotkeyMatcher(Previous), new HotkeyCaptureCoordinator(_time));

        var pending = handler.Handle(new CaptureHotkeyRequest(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(9));
        pending.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(1));

        (await pending).ShouldBeNull();
    }

    [Fact]
    public async Task GetMicrophones_ReturnsTheActiveCaptureDevices()
    {
        var microphones = new[] { new MicrophoneInfo("mic-1", "Headset", IsDefault: true), new MicrophoneInfo("mic-2", "Webcam", IsDefault: false) };
        _recorder.ListMicrophones().Returns(microphones);

        var result = await new GetMicrophonesHandler(_recorder).Handle(new GetMicrophonesRequest(), CancellationToken.None);

        result.ShouldBe(microphones);
    }
}
