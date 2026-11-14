using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.App;

/// <summary>What the About dialog shows about this copy of Murmur and the history it holds.</summary>
public record GetAboutRequest : IRequest<AboutInfo>;

public class GetAboutHandler(IAppInfo app, IHistoryStore history) : IRequestHandler<GetAboutRequest, AboutInfo>
{
    public Task<AboutInfo> Handle(GetAboutRequest request, CancellationToken ct)
    {
        var totals = history.Totals();
        return Task.FromResult(new AboutInfo(
            app.Version, app.Channel, app.DataFolder, app.WindowsBuild, totals.Dictations, totals.Words, totals.Since));
    }
}
