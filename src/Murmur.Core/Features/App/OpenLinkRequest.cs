using MediatR;
using Murmur.Core.Models;
using Murmur.Core.Services;

namespace Murmur.Core.Features.App;

/// <summary>Opens one of the fixed links (see <see cref="AppLinks"/>) in the browser or in Windows Settings.</summary>
public record OpenLinkRequest(LinkTarget Target) : IRequest;

public class OpenLinkHandler(ILinkOpener opener) : IRequestHandler<OpenLinkRequest>
{
    public Task Handle(OpenLinkRequest request, CancellationToken ct)
    {
        opener.Open(AppLinks.For(request.Target));
        return Task.CompletedTask;
    }
}
