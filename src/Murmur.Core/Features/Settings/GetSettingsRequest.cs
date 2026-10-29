using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Settings;

public record GetSettingsRequest : IRequest<MurmurSettings>;

public class GetSettingsHandler(ISettingsService settingsService) : IRequestHandler<GetSettingsRequest, MurmurSettings>
{
    public Task<MurmurSettings> Handle(GetSettingsRequest request, CancellationToken ct)
    {
        return Task.FromResult(settingsService.Load());
    }
}
