using Mediator;
using Murmur.Core.Models;
using Murmur.Core.Speech;

namespace Murmur.Core.Features.Models;

/// <summary>The speech model in use, the one this PC suits, and the models on offer.</summary>
public record GetModelChoiceRequest : IRequest<ModelChoice>;

public class GetModelChoiceHandler(ModelSelection selection) : IRequestHandler<GetModelChoiceRequest, ModelChoice>
{
    public ValueTask<ModelChoice> Handle(GetModelChoiceRequest request, CancellationToken ct) =>
        ValueTask.FromResult(new ModelChoice(selection.ResolveActive(), selection.Recommended, SpeechModelCatalog.Options));
}
