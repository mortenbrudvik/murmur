using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Serilog;

namespace Murmur.Core.Setup;

/// <summary>
/// Decides once per start whether the first-run setup is shown, and keeps its progress in the settings so that a
/// restart resumes where the user left off.
/// </summary>
public sealed class SetupCoordinator(ISettingsService settings, ModelSelection selection, IModelManager models, LaunchOptions launch)
{
    private readonly ILogger _logger = Log.ForContext<SetupCoordinator>();
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _openPageLock = new();
    private int _started;

    /// <summary>Completes once <see cref="Initialize"/> has ended, also when it failed.</summary>
    public Task Initialized => _initialized.Task;

    /// <summary>
    /// Makes the first-run decision and saves it. Runs once and reads files, so call it off the UI thread and before
    /// the speech engine starts (which would otherwise choose the model first). A failure is rethrown, after
    /// <see cref="Initialized"/> has completed.
    /// </summary>
    public void Initialize()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            Decide();
        }
        finally
        {
            _initialized.TrySetResult();
        }
    }

    /// <summary>Waits for <see cref="Initialize"/>. The launch's open-page request is part of the answer once only.</summary>
    public async Task<SetupState> GetStateAsync()
    {
        await Initialized.ConfigureAwait(false);
        return ToState(settings.Load(), TakeOpenPage());
    }

    /// <summary>Saves the fields that are not null and leaves the rest as they are.</summary>
    public SetupState Update(SetupStep? step, bool? cudaTipDismissed, bool? completed)
    {
        var updated = settings.Update(s => s with
        {
            SetupStep = step ?? s.SetupStep,
            CudaTipDismissed = cudaTipDismissed ?? s.CudaTipDismissed,
            SetupCompleted = completed ?? s.SetupCompleted,
        });
        return ToState(updated, openPage: null);
    }

    private void Decide()
    {
        var current = settings.Load();
        if (current.SetupCompleted || current.SetupStep is not null)
        {
            _logger.Information("Setup: {Status}", current.SetupCompleted ? "already completed" : $"resuming at {current.SetupStep}");
            return;
        }

        // Neither a finished setup nor one in progress: a fresh profile, or someone upgrading from 1.0, which wrote
        // neither. The expected file size is enough here; the full hash check still runs before the model is loaded.
        var active = selection.ResolveActive();
        var present = models.HasExpectedFile(SpeechModelCatalog.Get(active));

        // A change saved since the Load above wins over this decision.
        settings.Update(s => s.SetupCompleted || s.SetupStep is not null
            ? s
            : present ? s with { SetupCompleted = true } : s with { SetupStep = SetupStep.SpeechModel });
        _logger.Information("Setup: {Status} (model {Model}, file present: {Present})",
            present ? "not needed, the speech model is already there" : "first run", active, present);
    }

    private string? TakeOpenPage()
    {
        lock (_openPageLock)
        {
            var page = launch.OpenPage;
            launch.OpenPage = null;
            return page;
        }
    }

    // SetupStep is only null when Initialize failed; the first step is where a fresh start begins anyway.
    private static SetupState ToState(MurmurSettings s, string? openPage) =>
        new(!s.SetupCompleted, s.SetupStep ?? SetupStep.SpeechModel, s.CudaTipDismissed, openPage);
}
