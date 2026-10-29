using MediatR;
using Murmur.Core.Input;
using Murmur.Core.Models;

namespace Murmur.Core.Features.Settings;

/// <summary>Waits for the user to press a new hotkey. Returns null on Esc or after 10 seconds without one.</summary>
public record CaptureHotkeyRequest : IRequest<HotkeyDefinition?>;

public class CaptureHotkeyHandler(HotkeyMatcher matcher, HotkeyCaptureCoordinator coordinator)
    : IRequestHandler<CaptureHotkeyRequest, HotkeyDefinition?>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public Task<HotkeyDefinition?> Handle(CaptureHotkeyRequest request, CancellationToken ct)
    {
        // The user just clicked a button, so a hotkey still marked as held is stale and would make the next press look
        // like auto-repeat.
        matcher.ResetHeld();
        return coordinator.CaptureAsync(Timeout, ct);
    }
}
