using Murmur.Core.Models;

namespace Murmur.Core.Input;

/// <summary>
/// Runs a <see cref="HotkeyCaptureSession"/> against the live keyboard hook. While a capture is active,
/// <see cref="TryHandle"/> consumes every physical key event so that the keys being recorded neither reach other apps
/// nor trigger dictation. <see cref="CaptureAsync"/> may be called from any thread; <see cref="TryHandle"/> is called
/// from the keyboard hook.
/// </summary>
/// <param name="layout">Names a captured symbol key after the character the current layout puts on it.</param>
public sealed class HotkeyCaptureCoordinator(TimeProvider time, IKeyboardLayout layout)
{
    private readonly Lock _gate = new();
    private PendingCapture? _active;

    /// <summary>
    /// Waits for the user to press a new hotkey. Returns null on Esc, on timeout, or when superseded by a newer capture;
    /// throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task<HotkeyDefinition?> CaptureAsync(TimeSpan timeout, CancellationToken ct)
    {
        // Created first: an invalid timeout must throw before the capture starts swallowing keys.
        using var timeoutCts = new CancellationTokenSource(timeout, time);

        var capture = new PendingCapture(layout);
        lock (_gate)
        {
            _active?.Completion.TrySetResult(null);
            _active = capture;
        }

        using var onTimeout = timeoutCts.Token.Register(() => Finish(capture, null));
        using var onCancel = ct.Register(() => Cancel(capture, ct));

        return await capture.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Ends the active capture, if any, as if Esc had been pressed (its <see cref="CaptureAsync"/> returns null), so
    /// keys stop being swallowed. Called when the window that asked for the capture loses focus or hides: keys typed
    /// into another app afterwards must not become the hotkey. May be called from any thread.
    /// </summary>
    public void Cancel()
    {
        PendingCapture? capture;
        lock (_gate)
        {
            capture = _active;
            _active = null;
        }

        capture?.Completion.TrySetResult(null);
    }

    /// <summary>
    /// Feeds a key event to the active capture. Returns true (swallow the event) while a capture is active.
    /// Injected input is never captured and always passes through, so Murmur's own paste keeps working.
    /// </summary>
    public bool TryHandle(KeyInput input)
    {
        lock (_gate)
        {
            if (_active is not { } capture || input.IsInjected)
                return false;

            capture.Session.OnKey(input);
            if (capture.Session.IsComplete)
            {
                _active = null;
                capture.Completion.TrySetResult(capture.Session.Result);
            }

            return true;
        }
    }

    private void Finish(PendingCapture capture, HotkeyDefinition? result)
    {
        Deactivate(capture);
        capture.Completion.TrySetResult(result);
    }

    private void Cancel(PendingCapture capture, CancellationToken ct)
    {
        Deactivate(capture);
        capture.Completion.TrySetCanceled(ct);
    }

    private void Deactivate(PendingCapture capture)
    {
        lock (_gate)
        {
            if (_active == capture)
                _active = null;
        }
    }

    private sealed class PendingCapture(IKeyboardLayout layout)
    {
        public HotkeyCaptureSession Session { get; } = new(layout);

        // Continuations run asynchronously so completing a capture never runs caller code inside the keyboard hook.
        public TaskCompletionSource<HotkeyDefinition?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
