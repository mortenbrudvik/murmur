using System.ComponentModel;
using Murmur.Core.Input;
using Serilog;

namespace Murmur.Core.Insertion;

/// <summary>
/// Pastes text through the clipboard: snapshot the owner's clipboard, put the text on it, send Ctrl+V once no
/// modifier is held, then put the snapshot back after <see cref="RestoreDelay"/> unless something else has written
/// the clipboard meanwhile. <see cref="InsertAsync"/> returns as soon as Ctrl+V is sent; the restore finishes in the
/// background, still holding the gate, so the next insert (or copy) waits for it. Into an elevated window, where
/// Windows blocks injected input, the text is just left on the clipboard.
/// </summary>
public sealed class TextInserter(IClipboard clipboard, IForegroundInfo foreground, IKeyboardSender keyboard,
    IKeyStateProbe keys, TimeProvider time) : ITextInserter
{
    /// <summary>How long the target app gets to read the clipboard after Ctrl+V before the snapshot goes back.</summary>
    internal static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(500);

    internal static readonly TimeSpan ModifierPollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>After this long with a modifier still held, paste anyway rather than lose the text.</summary>
    internal static readonly TimeSpan ModifierWaitLimit = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger = Log.ForContext<TextInserter>();

    // One insert at a time, held until its restore is done: a second insert must not snapshot the first one's text as
    // "the owner's clipboard".
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The background restore of the last paste, or a completed task. It never faults.</summary>
    internal Task PendingRestore { get; private set; } = Task.CompletedTask;

    public async Task<InsertResult> InsertAsync(string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var releaseGate = true;
        try
        {
            var target = foreground.Capture();
            if (target.IsElevated)
            {
                clipboard.SetText(text);
                _logger.Information("{App} is elevated; left {Length} characters on the clipboard", target.AppName, text.Length);
                return new InsertResult(InsertOutcome.LeftOnClipboard, target.AppName);
            }

            var snapshot = clipboard.Capture();
            clipboard.SetText(text);
            var sequence = clipboard.SequenceNumber;

            try
            {
                await WaitForModifiersReleasedAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                TryRestore(snapshot);
                throw;
            }

            try
            {
                keyboard.SendCtrlV();
            }
            catch (Win32Exception ex)
            {
                _logger.Warning(ex, "Could not send Ctrl+V to {App}; left {Length} characters on the clipboard", target.AppName, text.Length);
                return new InsertResult(InsertOutcome.LeftOnClipboard, target.AppName);
            }

            _logger.Debug("Pasted {Length} characters into {App}", text.Length, target.AppName);

            // The text is pasted, so cancellation no longer applies: the owner's clipboard always comes back. The
            // restore takes over the gate and releases it when done.
            PendingRestore = RestoreAfterDelayAsync(snapshot, sequence);
            releaseGate = false;
            return new InsertResult(InsertOutcome.Pasted, target.AppName);
        }
        finally
        {
            if (releaseGate)
                _gate.Release();
        }
    }

    public bool TryCopy(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        _gate.Wait();
        try
        {
            clipboard.SetText(text);
            _logger.Debug("Left {Length} characters on the clipboard", text.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not put {Length} characters on the clipboard", text.Length);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Waits <see cref="RestoreDelay"/>, puts the snapshot back if nothing else wrote the clipboard, then releases the gate.</summary>
    private async Task RestoreAfterDelayAsync(ClipboardSnapshot snapshot, uint sequence)
    {
        try
        {
            await Task.Delay(RestoreDelay, time, CancellationToken.None).ConfigureAwait(false);

            if (clipboard.SequenceNumber == sequence)
                TryRestore(snapshot);
            else
                _logger.Debug("Clipboard changed after the paste; not restoring the previous contents");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not restore the previous clipboard contents");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Polls until no modifier is held (so the target gets Ctrl+V, not Ctrl+Shift+V), for at most <see cref="ModifierWaitLimit"/>.</summary>
    private async Task WaitForModifiersReleasedAsync(CancellationToken ct)
    {
        var start = time.GetTimestamp();
        while (keys.IsAnyModifierDown())
        {
            if (time.GetElapsedTime(start) >= ModifierWaitLimit)
            {
                _logger.Warning("A modifier key is still held after {Limit}; pasting anyway", ModifierWaitLimit);
                return;
            }

            await Task.Delay(ModifierPollInterval, time, ct).ConfigureAwait(false);
        }
    }

    private void TryRestore(ClipboardSnapshot snapshot)
    {
        try
        {
            clipboard.Restore(snapshot);
        }
        catch (Win32Exception ex)
        {
            _logger.Warning(ex, "Could not restore the previous clipboard contents");
        }
    }
}
