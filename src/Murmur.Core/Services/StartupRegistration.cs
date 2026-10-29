using Microsoft.Win32;
using Serilog;

namespace Murmur.Core.Services;

/// <summary>
/// Starts Murmur at sign-in through the per-user Run key (HKCU, no admin rights), as
/// <c>"{exePath}" --minimized</c> so it comes up in the tray without opening its window.
/// </summary>
public sealed class StartupRegistration(string exePath,
    string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", string valueName = "Murmur") : IStartupRegistration
{
    private readonly ILogger _logger = Log.ForContext<StartupRegistration>();
    private readonly string _command = $"\"{exePath}\" --minimized";

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
            return IsThisCommand(key?.GetValue(valueName));
        }
    }

    public void Apply(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true);
            if (IsThisCommand(key.GetValue(valueName)))
                return;

            key.SetValue(valueName, _command, RegistryValueKind.String);
            _logger.Information("Registered to start with Windows: {Command}", _command);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (key?.GetValue(valueName) is null)
                return;

            key.DeleteValue(valueName, throwOnMissingValue: false);
            _logger.Information("Removed the start-with-Windows entry");
        }
    }

    /// <summary>Windows paths are case-insensitive, so a differently cased entry still starts this executable.</summary>
    private bool IsThisCommand(object? value) =>
        value is string command && string.Equals(command, _command, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Used by Debug builds, so running the app from a development folder never adds that folder's executable to the
/// owner's sign-in programs.
/// </summary>
public sealed class NoopStartupRegistration : IStartupRegistration
{
    private readonly ILogger _logger = Log.ForContext<NoopStartupRegistration>();

    public bool IsRegistered => false;

    public void Apply(bool enabled) =>
        _logger.Debug("Debug build: start with Windows ({Enabled}) is not applied", enabled);
}
