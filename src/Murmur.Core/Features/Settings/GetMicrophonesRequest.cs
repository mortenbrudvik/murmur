using Mediator;
using Murmur.Core.Audio;
using Murmur.Core.Models;

namespace Murmur.Core.Features.Settings;

public record GetMicrophonesRequest : IRequest<IReadOnlyList<MicrophoneInfo>>;

public class GetMicrophonesHandler(IAudioRecorder recorder) : IRequestHandler<GetMicrophonesRequest, IReadOnlyList<MicrophoneInfo>>
{
    public ValueTask<IReadOnlyList<MicrophoneInfo>> Handle(GetMicrophonesRequest request, CancellationToken ct)
    {
        return ValueTask.FromResult(recorder.ListMicrophones());
    }
}
