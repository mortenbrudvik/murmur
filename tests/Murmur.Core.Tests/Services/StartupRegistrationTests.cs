using Microsoft.Win32;
using Murmur.Core.Services;
using Shouldly;

namespace Murmur.Core.Tests.Services;

/// <summary>Writes to a throwaway HKCU key, never the real Run key, and deletes it after each test.</summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private const string TestRootKey = @"Software\MurmurTests";
    private const string TestRunKey = @"Software\MurmurTests\Run";
    private const string ValueName = "Murmur";
    private const string ExePath = @"C:\x\Murmur.UI.exe";

    private readonly StartupRegistration _registration = new(ExePath, TestRunKey, ValueName);

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(TestRootKey, throwOnMissingSubKey: false);

    [Fact]
    public void Apply_True_WritesQuotedPathWithMinimized()
    {
        _registration.Apply(true);

        ReadValue().ShouldBe("\"C:\\x\\Murmur.UI.exe\" --minimized");
        _registration.IsRegistered.ShouldBeTrue();
    }

    [Fact]
    public void Apply_False_RemovesValue()
    {
        _registration.Apply(true);

        _registration.Apply(false);

        ReadValue().ShouldBeNull();
        _registration.IsRegistered.ShouldBeFalse();
    }

    [Fact]
    public void Apply_False_WhenAbsent_DoesNotThrow()
    {
        Should.NotThrow(() => _registration.Apply(false));

        _registration.IsRegistered.ShouldBeFalse();
    }

    [Fact]
    public void Apply_True_ReplacesEntryForAnotherPath()
    {
        new StartupRegistration(@"C:\old\Murmur.UI.exe", TestRunKey, ValueName).Apply(true);

        _registration.Apply(true);

        ReadValue().ShouldBe("\"C:\\x\\Murmur.UI.exe\" --minimized");
    }

    [Fact]
    public void IsRegistered_EntryForAnotherPath_False()
    {
        new StartupRegistration(@"C:\old\Murmur.UI.exe", TestRunKey, ValueName).Apply(true);

        _registration.IsRegistered.ShouldBeFalse();
    }

    private static object? ReadValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(TestRunKey);
        return key?.GetValue(ValueName);
    }
}
