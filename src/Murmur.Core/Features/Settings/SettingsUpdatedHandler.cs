using MediatR;
using Murmur.Core.Input;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Features.Settings;

/// <summary>
/// Puts saved settings to work: the new hotkey takes effect at once, and the start-with-Windows entry follows the
/// checkbox. The microphone, sound cues and audio-keeping choices need nothing here because each dictation reads them
/// when it starts.
/// </summary>
public class SettingsUpdatedHandler(HotkeyMatcher matcher, IStartupRegistration startup)
    : INotificationHandler<SettingsUpdatedNotification>
{
    private static readonly ILogger Logger = Log.ForContext<SettingsUpdatedHandler>();

    public Task Handle(SettingsUpdatedNotification notification, CancellationToken ct)
    {
        var settings = notification.Settings;
        matcher.Hotkey = settings.Hotkey;

        try
        {
            startup.Apply(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            // The settings are already saved; a registry failure (for example by policy) must not fail the save.
            Logger.Warning(ex, "Could not update the start-with-Windows entry");
        }

        return Task.CompletedTask;
    }
}
