using MediatR;
using Murmur.Common;
using Murmur.Core.Models;
using Murmur.Core.Speech;

namespace Murmur.Core.Features.Models;

/// <summary>Makes <paramref name="Model"/> the speech model; the download and the switch go on in the background.</summary>
public record SetModelChoiceRequest(SpeechModelId Model) : IRequest<ModelChoice>;

public class SetModelChoiceHandler(ISpeechEngine engine, ModelSelection selection)
    : IRequestHandler<SetModelChoiceRequest, ModelChoice>
{
    public Task<ModelChoice> Handle(SetModelChoiceRequest request, CancellationToken ct)
    {
        // The choice is saved before the call returns, but the returned task only ends when the whole switch has,
        // after the download and the load. Progress reaches the web through the engine status.
        engine.SwitchModelAsync(request.Model).FireAndForget();

        return Task.FromResult(new ModelChoice(request.Model, selection.Recommended, SpeechModelCatalog.Options));
    }
}
