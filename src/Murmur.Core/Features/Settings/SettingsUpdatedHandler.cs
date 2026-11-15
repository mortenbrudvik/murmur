using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Input;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Features.Settings;

/// <summary>
/// Puts saved settings to work: the new hotkey takes effect at once, the start-with-Windows entry follows the
/// checkbox, and the microphone is opened or released as "keep the microphone ready" says (for the microphone now
/// chosen). The sound cues and audio-keeping choices need nothing here because each dictation reads them when it
/// starts, as it does the microphone when it is not kept ready.
/// </summary>
public class SettingsUpdatedHandler(HotkeyMatcher matcher, IStartupRegistration startup, MicrophoneReadiness microphone)
    : INotificationHandler<SettingsUpdatedNotification>
{
    private static readonly ILogger Logger = Log.ForContext<SettingsUpdatedHandler>();

    public Task Handle(SettingsUpdatedNotification notification, CancellationToken ct)
    {
        var settings = notification.Settings;
        matcher.Hotkey = settings.Hotkey;

        // Returns at once: the microphone opens, or is released, in the background.
        microphone.Apply(settings);

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
