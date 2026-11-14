using MediatR;
using Murmur.Core.Audio;
using Murmur.Core.Features.History;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.Core.Text;
using Serilog;

namespace Murmur.Core.Dictation;

/// <summary>
/// Turns a recording into text and gets the text to the owner: transcribes and post-processes it, pastes it, and keeps
/// it in history (with its audio, when kept) or, when that fails too, on the clipboard. Tells the owner nothing: the
/// <see cref="DictationController"/> shows the outcome it returns.
/// <para>
/// Called on the controller's thread, and continues there (it uses no <c>ConfigureAwait(false)</c>), so a new history
/// entry is announced on the UI thread. The paste, the copy and the history and audio file writes run on the thread
/// pool.
/// </para>
/// </summary>
public sealed class DictationDelivery
{
    /// <summary>
    /// Whisper's own silence verdict. The speech detector is the real gate (Whisper.net reports 0 here); this is a
    /// harmless second guard.
    /// </summary>
    private const float NoSpeechThreshold = 0.6f;

    private readonly ILogger _logger = Log.ForContext<DictationDelivery>();
    private readonly ISpeechEngine _engine;
    private readonly ITranscriptPostProcessor[] _postProcessors;
    private readonly ITextInserter _inserter;
    private readonly IHistoryStore _history;
    private readonly IAudioArchive _audioArchive;
    private readonly IVocabularyService _vocabulary;
    private readonly IMediator _mediator;
    private readonly TimeProvider _time;

    public DictationDelivery(
        ISpeechEngine engine, IEnumerable<ITranscriptPostProcessor> postProcessors, ITextInserter inserter,
        IHistoryStore history, IAudioArchive audioArchive, IVocabularyService vocabulary, IMediator mediator,
        TimeProvider time)
    {
        _engine = engine;
        _postProcessors = [.. postProcessors.OrderBy(p => p.Order)];
        _inserter = inserter;
        _history = history;
        _audioArchive = audioArchive;
        _vocabulary = vocabulary;
        _mediator = mediator;
        _time = time;
    }

    /// <summary>Transcribes <paramref name="samples"/>, prompted with the vocabulary's hint words, and post-processes the text.</summary>
    /// <returns>The post-processed transcript, or "" when there was no speech in it.</returns>
    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        var prompt = PromptBuilder.Build(_vocabulary.Load().HintWords);
        var result = await _engine.TranscribeAsync(samples, prompt, ct);
        if (result.MaxNoSpeechProbability >= NoSpeechThreshold)
            return "";

        var text = result.Text;
        foreach (var postProcessor in _postProcessors)
            text = postProcessor.Process(text);

        // Trimmed last: a vocabulary rule that deletes a leading or trailing word ("um" → "") leaves a space behind.
        return text.Trim();
    }

    /// <summary>
    /// Pastes <paramref name="text"/> into the focused app and records it in history. When the paste fails, keeps the
    /// text as <see cref="KeepUnpastedAsync"/> does, unless the dictation has been cancelled meanwhile.
    /// </summary>
    /// <param name="samples">The recording, kept with the history entry when <paramref name="keepAudio"/>.</param>
    /// <param name="isCurrent">Whether the dictation is still the current one; false once Esc has cancelled it.</param>
    /// <param name="ct">Cancels the paste up to the moment Ctrl+V is sent.</param>
    /// <returns>
    /// <see cref="DictationOutcome.Pasted"/> or <see cref="DictationOutcome.LeftOnClipboard"/> with the target app and the
    /// time of the paste; after a failed paste, the outcome of keeping the text, with no target app or time.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before Ctrl+V was sent.</exception>
    public async Task<DeliveryResult> PasteAsync(
        string text, float[] samples, bool keepAudio, Func<bool> isCurrent, CancellationToken ct)
    {
        InsertResult inserted;
        try
        {
            // Off the UI thread: clipboard retries and the wait for modifiers to be released would freeze it.
            inserted = await Task.Run(() => _inserter.InsertAsync(text, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.Error(ex, "Could not paste the transcript");
            return new DeliveryResult(
                isCurrent() ? await KeepUnpastedAsync(text, samples, keepAudio, isCurrent) : DictationOutcome.Cancelled);
        }

        // The text has left Murmur (it is in the target app, or on the clipboard), so it goes into history even if Esc
        // was pressed in the meantime.
        var insertedAt = _time.GetUtcNow();
        var pasted = inserted.Outcome == InsertOutcome.Pasted;

        // A failed save is only logged: the text is already where the owner wanted it.
        await SaveAsync(text, inserted.TargetApp, pasted, keepAudio ? samples : null);
        return new DeliveryResult(
            pasted ? DictationOutcome.Pasted : DictationOutcome.LeftOnClipboard, inserted.TargetApp, insertedAt);
    }

    /// <summary>
    /// For text that was not pasted (a hands-free session that hit its time limit, or a failed paste): keeps it in
    /// history, or when that fails and the dictation has not been cancelled meanwhile, on the clipboard.
    /// </summary>
    /// <param name="samples">The recording, kept with the history entry when <paramref name="keepAudio"/>.</param>
    /// <param name="isCurrent">Whether the dictation is still the current one; false once Esc has cancelled it.</param>
    /// <returns>
    /// <see cref="DictationOutcome.SavedOnly"/>, <see cref="DictationOutcome.LeftOnClipboard"/>,
    /// <see cref="DictationOutcome.Failed"/> when the text could not be copied either, or
    /// <see cref="DictationOutcome.Cancelled"/> when the save failed after the dictation was cancelled.
    /// </returns>
    public async Task<DictationOutcome> KeepUnpastedAsync(string text, float[] samples, bool keepAudio, Func<bool> isCurrent)
    {
        if (await SaveAsync(text, "", pasted: false, keepAudio ? samples : null))
            return DictationOutcome.SavedOnly;

        if (!isCurrent())
            return DictationOutcome.Cancelled;

        // Off the UI thread: the copy waits for any clipboard restore still pending.
        var copied = await Task.Run(() => _inserter.TryCopy(text));
        return copied ? DictationOutcome.LeftOnClipboard : DictationOutcome.Failed;
    }

    /// <summary>
    /// Records the dictation in history (with its audio, when kept) and announces it. The entry and its audio are written
    /// on the thread pool; the announcement goes out on the caller's thread once both are done. Failures are logged,
    /// never thrown.
    /// </summary>
    /// <param name="samples">The audio to keep with the entry, or null when audio is not kept.</param>
    /// <returns>Whether the entry is in history; losing only its audio or its announcement still counts as saved.</returns>
    private async Task<bool> SaveAsync(string text, string targetApp, bool pasted, float[]? samples)
    {
        var entry = await Task.Run(() => Store(text, targetApp, pasted, samples));
        if (entry is null)
            return false;

        try
        {
            await _mediator.Publish(new HistoryEntryAddedNotification(entry));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not announce history entry {EntryId}", entry.Id);
        }

        return true;
    }

    /// <summary>Adds the history entry, then saves its audio. Failures are logged, never thrown.</summary>
    /// <param name="samples">The audio to keep with the entry, or null when audio is not kept.</param>
    /// <returns>The entry, or null when it could not be added.</returns>
    private HistoryEntry? Store(string text, string targetApp, bool pasted, float[]? samples)
    {
        HistoryEntry entry;
        try
        {
            entry = _history.Add(text, targetApp, pasted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not save the dictation to history");
            return null;
        }

        if (samples is not null)
        {
            try
            {
                _audioArchive.Save(entry.Id, samples);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the audio of history entry {EntryId}", entry.Id);
            }
        }

        return entry;
    }
}

/// <summary>Where <see cref="DictationDelivery.PasteAsync"/> got the text to.</summary>
/// <param name="TargetApp">The app the text was pasted into (or meant for); empty when the paste failed.</param>
/// <param name="InsertedAt">When Ctrl+V was sent, or the text was left on the clipboard; null when the paste failed.</param>
public sealed record DeliveryResult(DictationOutcome Outcome, string TargetApp = "", DateTimeOffset? InsertedAt = null);
