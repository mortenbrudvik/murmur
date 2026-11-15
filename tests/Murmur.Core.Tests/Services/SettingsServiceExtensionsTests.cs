using Murmur.Core.Models;
using Murmur.Core.Services;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class SettingsServiceExtensionsTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void ReadHotkeyName_SettingsRead_IsTheHotkeysDisplayName()
    {
        _settings.Load().Returns(MurmurSettings.Default with
        {
            Hotkey = new HotkeyDefinition(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+Space"),
        });

        _settings.ReadHotkeyName(fallback: "Right Ctrl").ShouldBe("Ctrl+Shift+Space");
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void ReadHotkeyName_SettingsCannotBeRead_IsTheFallback(Type failure)
    {
        _settings.Load().Returns(_ => throw (Exception)Activator.CreateInstance(failure, "settings.json is locked")!);

        _settings.ReadHotkeyName(fallback: "Left Alt").ShouldBe("Left Alt");
    }

    [Fact]
    public void ReadHotkeyName_AnyOtherFailure_Throws()
    {
        _settings.Load().Returns(_ => throw new InvalidOperationException("a bug"));

        Should.Throw<InvalidOperationException>(() => _settings.ReadHotkeyName(fallback: "Right Ctrl"));
    }
}
