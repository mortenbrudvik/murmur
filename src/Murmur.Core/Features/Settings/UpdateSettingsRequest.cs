using MediatR;
using Murmur.Core.Input;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Settings;

public record UpdateSettingsRequest(
    HotkeyDefinition Hotkey,
    string? MicrophoneId,
    bool SoundCues,
    bool StartWithWindows,
    bool KeepAudio,
    bool KeepMicrophoneReady = false) : IRequest<MurmurSettings>;

/// <summary>Published after the settings were saved, so the running app can adopt them.</summary>
public sealed class SettingsUpdatedNotification(MurmurSettings settings) : INotification
{
    public MurmurSettings Settings { get; } = settings;
}

/// <summary>
/// Saves the settings and announces them. A hotkey that is missing or not usable keeps the previous one, and a usable
/// one is saved under the name made here from its key and modifiers, as the capture names it, not the name the page
/// sent. A blank microphone id means the Windows default (null). Only the fields in the request change; the setup and
/// model fields stay as they are stored.
/// </summary>
/// <param name="layout">Names the hotkey's key the way <see cref="HotkeyCaptureSession"/> does.</param>
public class UpdateSettingsHandler(ISettingsService settingsService, IMediator mediator, IKeyboardLayout layout)
    : IRequestHandler<UpdateSettingsRequest, MurmurSettings>
{
    public async Task<MurmurSettings> Handle(UpdateSettingsRequest request, CancellationToken ct)
    {
        var microphoneId = string.IsNullOrWhiteSpace(request.MicrophoneId) ? null : request.MicrophoneId;
        var hotkey = request.Hotkey is { IsValid: true } requested
            ? requested with { DisplayName = KeyNames.Describe(requested.VirtualKey, requested.Modifiers, layout) }
            : null;

        var settings = settingsService.Update(current => current with
        {
            Hotkey = hotkey ?? current.Hotkey,
            MicrophoneId = microphoneId,
            SoundCues = request.SoundCues,
            StartWithWindows = request.StartWithWindows,
            KeepAudio = request.KeepAudio,
            KeepMicrophoneReady = request.KeepMicrophoneReady,
        });
        await mediator.Publish(new SettingsUpdatedNotification(settings), ct).ConfigureAwait(false);
        return settings;
    }
}
