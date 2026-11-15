using Mediator;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.Settings;

public record GetSettingsRequest : IRequest<MurmurSettings>;

public class GetSettingsHandler(ISettingsService settingsService) : IRequestHandler<GetSettingsRequest, MurmurSettings>
{
    public ValueTask<MurmurSettings> Handle(GetSettingsRequest request, CancellationToken ct)
    {
        return ValueTask.FromResult(settingsService.Load());
    }
}
