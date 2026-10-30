using MediatR;
using Murmur.Common;
using Murmur.Core.Models;
using Murmur.Core.Speech;

namespace Murmur.Core.Features.Models;

/// <summary>
/// Makes <paramref name="Model"/> the speech model; the download and the switch go on in the background. Fails when the
/// choice cannot be saved.
/// </summary>
public record SetModelChoiceRequest(SpeechModelId Model) : IRequest<ModelChoice>;

public class SetModelChoiceHandler(ISpeechEngine engine, ModelSelection selection)
    : IRequestHandler<SetModelChoiceRequest, ModelChoice>
{
    public async Task<ModelChoice> Handle(SetModelChoiceRequest request, CancellationToken ct)
    {
        // The choice is saved before SwitchModelAsync returns, and a failure to save it has already failed the task:
        // awaiting it then fails this call. Otherwise the task only ends when the whole switch has, after the download
        // and the load, which this does not wait for; progress reaches the web through the engine status.
        var switching = engine.SwitchModelAsync(request.Model);
        if (switching.IsCompleted)
            await switching.ConfigureAwait(false);
        else
            switching.FireAndForget();

        return new ModelChoice(request.Model, selection.Recommended, SpeechModelCatalog.Options);
    }
}
