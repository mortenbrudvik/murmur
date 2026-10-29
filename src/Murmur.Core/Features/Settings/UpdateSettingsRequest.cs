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
/// microphone id means the Windows default (null).
/// </summary>
public class UpdateSettingsHandler(ISettingsService settingsService, IMediator mediator)
    : IRequestHandler<UpdateSettingsRequest, MurmurSettings>
{
    public async Task<MurmurSettings> Handle(UpdateSettingsRequest request, CancellationToken ct)
    {
        var hotkey = request.Hotkey is { IsValid: true } ? request.Hotkey : settingsService.Load().Hotkey;
        var microphoneId = string.IsNullOrWhiteSpace(request.MicrophoneId) ? null : request.MicrophoneId;

        var settings = new MurmurSettings(hotkey, microphoneId, request.SoundCues, request.StartWithWindows, request.KeepAudio);
        settingsService.Save(settings);
        await mediator.Publish(new SettingsUpdatedNotification(settings), ct).ConfigureAwait(false);
        return settings;
    }
}
