using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Models;

namespace Murmur.Core.Features.Setup;

/// <summary>
/// Opens the microphone (the given one, or the Windows default when null) so the setup page can show its level, and
/// reports whether that worked. Levels follow as <see cref="MicLevelNotification"/>s until the test is stopped.
/// </summary>
public record StartMicTestRequest(string? MicrophoneId) : IRequest<MicTestResult>;

public class StartMicTestHandler(MicrophoneLevelMonitor monitor) : IRequestHandler<StartMicTestRequest, MicTestResult>
{
    public Task<MicTestResult> Handle(StartMicTestRequest request, CancellationToken ct) =>
        Task.FromResult(monitor.Start(request.MicrophoneId));
}

/// <summary>Ends the microphone test and releases the microphone. Harmless when no test is running.</summary>
public record StopMicTestRequest : IRequest;

public class StopMicTestHandler(MicrophoneLevelMonitor monitor) : IRequestHandler<StopMicTestRequest>
{
    public Task Handle(StopMicTestRequest request, CancellationToken ct)
    {
        monitor.Stop();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The current microphone level during a test, from 0 (silence) to 1 (full scale); the main window shows it as the
/// "setup.micLevel" event.
/// </summary>
public sealed class MicLevelNotification(double level) : INotification
{
    public double Level { get; } = level;
}
