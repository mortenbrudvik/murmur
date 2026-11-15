using Mediator;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.App;

/// <summary>
/// Opens one of the fixed links (see <see cref="AppLinks"/>) in the browser or in Windows Settings, or Murmur's data
/// folder in Explorer.
/// </summary>
public record OpenLinkRequest(LinkTarget Target) : IRequest;

public class OpenLinkHandler(ILinkOpener opener, MurmurPaths paths) : IRequestHandler<OpenLinkRequest>
{
    public ValueTask<Unit> Handle(OpenLinkRequest request, CancellationToken ct)
    {
        if (request.Target == LinkTarget.DataFolder)
        {
            // The folder is only made when something is first saved; opening a path that does not exist would fail.
            Directory.CreateDirectory(paths.Root);
            opener.Open(paths.Root);
        }
        else
        {
            opener.Open(AppLinks.For(request.Target));
        }

        return Unit.ValueTask;
    }
}
