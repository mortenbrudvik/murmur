using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Settings;

public record UpdateSettingsRequest(
    HotkeyDefinition Hotkey,
    string? MicrophoneId,
    bool SoundCues,
    bool StartWithWindows,
    bool KeepAudio) : IRequest<MurmurSettings>;

/// <summary>Published after the settings were saved, so the running app can adopt them.</summary>
public sealed class SettingsUpdatedNotification(MurmurSettings settings) : INotification
{
    public MurmurSettings Settings { get; } = settings;
}

/// <summary>
/// Saves the settings and announces them. A hotkey that is missing or not usable keeps the previous one, and a blank
/// microphone id means the Windows default (null). Only the fields in the request change; the setup and model fields
/// stay as they are stored.
/// </summary>
public class UpdateSettingsHandler(ISettingsService settingsService, IMediator mediator)
    : IRequestHandler<UpdateSettingsRequest, MurmurSettings>
{
    public async Task<MurmurSettings> Handle(UpdateSettingsRequest request, CancellationToken ct)
    {
        var microphoneId = string.IsNullOrWhiteSpace(request.MicrophoneId) ? null : request.MicrophoneId;

        var settings = settingsService.Update(current => current with
        {
            Hotkey = request.Hotkey is { IsValid: true } ? request.Hotkey : current.Hotkey,
            MicrophoneId = microphoneId,
            SoundCues = request.SoundCues,
            StartWithWindows = request.StartWithWindows,
            KeepAudio = request.KeepAudio,
        });
        await mediator.Publish(new SettingsUpdatedNotification(settings), ct).ConfigureAwait(false);
        return settings;
    }
}
