using System.Globalization;
using Murmur.Core.Audio;
using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>
/// The speech engine: the chosen Whisper model (<see cref="ModelSelection"/>), downloaded by <see cref="IModelManager"/>
/// and loaded by <see cref="IWhisperModelLoader"/>, on CUDA only when <see cref="GpuAdvisor.CanUseCuda"/> says the GPU
/// supports Murmur's CUDA build and <see cref="ICudaRuntimeLocator"/> found the CUDA 13 runtime. The status carries the
/// download's details and the tip about CUDA (<see cref="GpuAdvice"/>).
/// <para>
/// Starting and switching models are operations that run one at a time on the thread pool. A switch cancels the
/// running operation and waits for it to end before it touches any model file, because a download holds its .part file
/// open. Transcriptions use the loaded model under <c>_modelGate</c>, which an operation also takes to swap models, so
/// a model is never disposed while it transcribes. Whenever an operation reaches Ready on a model, it deletes every other
/// catalog model's files, so a switch that never finished (Murmur quit during its download, or its first load failed)
/// cannot leave a model on disk for good.
/// </para>
/// </summary>
public sealed class WhisperEngine(
    IModelManager models,
    ModelSelection selection,
    IGpuInfo gpu,
    ICudaRuntimeLocator cuda,
    IWhisperModelLoader loader,
    ISettingsService settings,
    TimeProvider time) : ISpeechEngine, IDisposable
{
    private const int SampleRate = 16000;

    /// <summary>Far below any real microphone's noise floor (about -70 dBFS), so only a dead input falls under it.</summary>
    private const float DigitalSilenceDbfs = -80f;

    /// <summary>How long <see cref="Dispose"/> waits for a running transcription before it leaves the model to process exit.</summary>
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger = Log.ForContext<WhisperEngine>();

    /// <summary>Serialises starting, cancelling and replacing operations. Operations themselves never take it.</summary>
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    /// <summary>Held by a transcription while it uses the loaded model, and by an operation while it swaps the model.</summary>
    private readonly SemaphoreSlim _modelGate = new(1, 1);

    /// <summary>
    /// Makes saving a requested model, restoring the previous one after a failed switch and checking that a switch is
    /// for the newest choice mutually exclusive.
    /// </summary>
    private readonly Lock _choiceLock = new();

    /// <summary>Cancelled by <see cref="Dispose"/>; every operation's token is linked to it.</summary>
    private readonly CancellationTokenSource _disposeCts = new();

    private EngineStatus _status = new(EngineState.NotStarted, "");
    private LoadedModel? _loaded;
    private int _disposed;

    // Under _operationGate.
    private Operation? _operation;
    private CancellationToken _lifetime;
    private bool _started;

    // Under _choiceLock: counts the requested choices, so only the newest one is switched to and a failed switch only
    // restores the choice it replaced.
    private int _choiceVersion;

    // Written by the first operation; operations never overlap, so later ones read it without a lock.
    private HardwareFacts? _hardware;

    public EngineStatus Status => Volatile.Read(ref _status);

    public event Action<EngineStatus>? StatusChanged;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Task running;
        try
        {
            if (IsDisposed || Volatile.Read(ref _loaded) is not null)
                return;

            _started = true;
            _lifetime = ct;
            running = _operation is { Task.IsCompleted: false } operation
                ? operation.Task
                : Start(target: null, choiceVersion: 0);
        }
        finally
        {
            _operationGate.Release();
        }

        await running.ConfigureAwait(false);
    }

    public async Task SwitchModelAsync(SpeechModelId model)
    {
        // Before the first await, so whoever asked for the switch reads the new choice straight back.
        if (!TrySaveChoice(model, out var target, out var choiceVersion))
            return;

        await SwitchToChoiceAsync(target, choiceVersion).ConfigureAwait(false);
    }

    public async Task<TranscriptionResult> TranscribeAsync(float[] samples16kMono, string prompt, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (Volatile.Read(ref _loaded) is null)
            throw new InvalidOperationException($"The speech engine is not ready (state: {Status.State}).");

        // Whisper invents text ("Thank you.") for digital silence, and the no-speech probability that
        // Whisper.net reports is always 0, so the model cannot flag it. Answer for it instead.
        if (SpeechDetector.RmsDbfs(samples16kMono) <= DigitalSilenceDbfs)
        {
            _logger.Debug("Skipping transcription of {Seconds:F1} s of digital silence", (double)samples16kMono.Length / SampleRate);
            return new TranscriptionResult("", 1f);
        }

        await _modelGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var loaded = Volatile.Read(ref _loaded)
                ?? throw new InvalidOperationException($"The speech engine is not ready (state: {Status.State}).");
            return await loaded.Model.TranscribeAsync(samples16kMono, prompt, ct).ConfigureAwait(false);
        }
        finally
        {
            _modelGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCts.Cancel();

        // Disposing native Whisper under a running transcription can crash the process.
        if (!_modelGate.Wait(DisposeWait))
        {
            _logger.Warning("A transcription is still running; its speech model is left to process exit");
            return;
        }

        try
        {
            Interlocked.Exchange(ref _loaded, null)?.Model.Dispose();
        }
        finally
        {
            _modelGate.Release();
        }
    }

    /// <summary>"Murmur needs 1.8 GB free on C:\ and there is 0.9 GB": decimal gigabytes with one decimal, like the web's formatBytes.</summary>
    internal static string DescribeShortfall(InsufficientDiskSpaceException ex)
    {
        var drive = Path.GetPathRoot(ex.Directory) is { Length: > 0 } root ? root : ex.Directory;
        return $"Murmur needs {Gigabytes(ex.RequiredBytes)} free on {drive} and there is {Gigabytes(ex.AvailableBytes)}";
    }

    private static string Gigabytes(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e9:0.0} GB");

    /// <summary>
    /// The first half of <see cref="SwitchModelAsync"/>: saves <paramref name="model"/> as the choice and numbers it.
    /// Internal, with <see cref="SwitchToChoiceAsync"/>, so a test can interleave two switches the way the thread pool can.
    /// </summary>
    internal bool TrySaveChoice(SpeechModelId model, out SpeechModelInfo target, out int choiceVersion)
    {
        try
        {
            target = SpeechModelCatalog.Get(model);
            lock (_choiceLock)
            {
                settings.Update(s => s with { SpeechModel = model });
                choiceVersion = ++_choiceVersion;
            }

            _logger.Information("Speech model chosen: {Model}", target.DisplayName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not save {Model} as the speech model", model);
            target = null!;
            choiceVersion = 0;
            return false;
        }
    }

    /// <summary>The second half of <see cref="SwitchModelAsync"/>: switches to a choice <see cref="TrySaveChoice"/> saved.</summary>
    internal async Task SwitchToChoiceAsync(SpeechModelInfo target, int choiceVersion)
    {
        Task switching;
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            switching = await BeginSwitchAsync(target, choiceVersion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not start switching the speech model to {Model}", target.DisplayName);
            return;
        }
        finally
        {
            _operationGate.Release();
        }

        await switching.ConfigureAwait(false);
    }

    /// <summary>
    /// Under <see cref="_operationGate"/>: cancels and awaits the running operation, deletes the partial download it
    /// leaves behind, and starts the operation that ends on <paramref name="target"/>, if any.
    /// </summary>
    private async Task<Task> BeginSwitchAsync(SpeechModelInfo target, int choiceVersion)
    {
        // Before InitializeAsync, the saved choice is all it takes: that is the model it loads.
        if (IsDisposed || !_started || _lifetime.IsCancellationRequested)
            return Task.CompletedTask;

        // A newer choice was saved while this request waited for the gate (requests run on the thread pool, so it may
        // even have switched already). Its request switches to it; switching to this one now would leave a model
        // running that is not the saved choice.
        bool superseded;
        lock (_choiceLock)
            superseded = choiceVersion != _choiceVersion;

        if (superseded)
        {
            _logger.Information("Not switching the speech model to {Model}: a newer choice was made", target.DisplayName);
            return Task.CompletedTask;
        }

        var previous = _operation;
        if (previous is not null)
        {
            previous.Superseded = true;
            Cancel(previous);
            await previous.Task.ConfigureAwait(false);
        }

        if (IsDisposed)
            return Task.CompletedTask;

        var loaded = Volatile.Read(ref _loaded);
        // A partial download goes now, to free its disk space for the next one. A complete file that was about to
        // become the first model stays until another model is Ready (DeleteModelsOtherThan), so a failed download
        // cannot leave the user with neither.
        if (previous?.Target is { } abandoned && abandoned != target && abandoned != loaded?.Info
            && (loaded is not null || !models.HasExpectedFile(abandoned)))
        {
            Delete(abandoned);
        }

        if (loaded is not null && loaded.Info == target)
        {
            // Already loaded: at most a switch away from it was cancelled above, or failed before.
            SetStatus(loaded.ReadyStatus);
            return Task.CompletedTask;
        }

        _logger.Information("Switching the speech model to {Model}", target.DisplayName);
        return Start(target, choiceVersion);
    }

    /// <summary>
    /// Under <see cref="_operationGate"/>, with no operation running: starts a first load when no model is loaded yet,
    /// otherwise a switch from the loaded one. A null <paramref name="target"/> means the model <see cref="ModelSelection"/> resolves.
    /// </summary>
    private Task Start(SpeechModelInfo? target, int choiceVersion)
    {
        _operation?.Cts.Dispose();

        var from = Volatile.Read(ref _loaded);
        var operation = new Operation(CancellationTokenSource.CreateLinkedTokenSource(_lifetime, _disposeCts.Token), choiceVersion)
        {
            Target = target,
        };
        operation.Task = Task.Run(async () =>
        {
            try
            {
                if (from is null)
                    await LoadFirstModelAsync(operation).ConfigureAwait(false);
                else
                    await SwitchFromAsync(operation, from).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Each operation reports its own failures; this only keeps a bug from faulting the callers' tasks.
                _logger.Error(ex, "A speech engine operation failed unexpectedly");
            }
        });
        _operation = operation;
        return operation.Task;
    }

    private async Task LoadFirstModelAsync(Operation operation)
    {
        var ct = operation.Cts.Token;
        HardwareFacts? hardware = null;
        SpeechModelInfo? model = null;
        LoadedModel next;
        var loading = false;
        try
        {
            hardware = Hardware();
            model = operation.Target ??= SpeechModelCatalog.Get(selection.ResolveActive());
            var loadingStatus = new EngineStatus(EngineState.Loading, model.DisplayName, Gpu: hardware.Advice);

            var target = model;
            var path = await EnsureModelAsync(model,
                download => new EngineStatus(EngineState.Downloading, target.DisplayName, Download: download, Gpu: hardware.Advice),
                whileVerifying: loadingStatus, ct).ConfigureAwait(false);

            loading = true;
            SetStatus(loadingStatus);
            next = await LoadAsync(model, path, hardware, ct).ConfigureAwait(false);
            if (hardware.RuntimeDirectory is not null && next.Model.Runtime?.StartsWith("Cuda", StringComparison.Ordinal) != true)
            {
                _logger.Warning("The CUDA 13 runtime is in {Directory}, but the speech engine runs on {Runtime}",
                    hardware.RuntimeDirectory, next.Model.Runtime ?? "an unknown runtime");
            }

            if (!await TrySwapInAsync(next, ct).ConfigureAwait(false))
                return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Information("Speech engine start-up was cancelled");
            // A switch that cancelled it reports the next model itself.
            if (!operation.Superseded && !IsDisposed)
                SetStatus(new EngineStatus(EngineState.NotStarted, model?.DisplayName ?? "", Gpu: hardware?.Advice));
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Speech engine failed to {Step} the speech model", loading ? "load" : "download");
            SetStatus(new EngineStatus(EngineState.Error, model?.DisplayName ?? "", Error: DescribeStartFailure(ex, model, loading),
                Gpu: hardware?.Advice));
            return;
        }

        // Outside the try: nothing after Ready may turn it into an Error.
        SetStatus(next.ReadyStatus);
        DeleteModelsOtherThan(next.Info);
    }

    private async Task SwitchFromAsync(Operation operation, LoadedModel from)
    {
        var ct = operation.Cts.Token;
        var target = operation.Target!;
        LoadedModel next;
        try
        {
            var hardware = Hardware();
            var path = await EnsureModelAsync(target, download => from.ReadyStatus with { Download = download }, whileVerifying: null, ct)
                .ConfigureAwait(false);

            SetStatus(new EngineStatus(EngineState.Loading, target.DisplayName, Gpu: from.Gpu));
            next = await LoadAsync(target, path, hardware, ct).ConfigureAwait(false);
            if (!await TrySwapInAsync(next, ct).ConfigureAwait(false))
                return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Information("Switching the speech model to {Model} was cancelled", target.DisplayName);
            if (!IsDisposed)
                SetStatus(from.ReadyStatus);
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not switch the speech model to {Model}; staying on {Previous}", target.DisplayName, from.Info.DisplayName);
            RestoreChoice(operation, from.Info);
            SetStatus(from.ReadyStatus with { Error = $"Could not switch to {target.DisplayName}: {DescribeSwitchFailure(ex)}" });
            return;
        }

        SetStatus(next.ReadyStatus);
        _logger.Information("Switched the speech model from {Previous} to {Model}", from.Info.DisplayName, target.DisplayName);

        // No transcription can hold the previous model any more: they take it under _modelGate, which the swap held.
        try
        {
            from.Model.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not unload {Model}", from.Info.DisplayName);
        }

        DeleteModelsOtherThan(target);
    }

    /// <summary>
    /// The verified model file, downloading what is missing. A download reports throttled progress through
    /// <paramref name="downloading"/>, the first report at once and from where it starts (the manager counts what a
    /// partial download already holds); a file that is there already reports <paramref name="whileVerifying"/>, when
    /// given, while it is verified (once per file: a second or two for the large model, with no progress to report).
    /// </summary>
    private Task<string> EnsureModelAsync(
        SpeechModelInfo model, Func<DownloadProgress, EngineStatus> downloading, EngineStatus? whileVerifying, CancellationToken ct)
    {
        var throttle = new DownloadReportThrottle(time);
        var progress = new InlineProgress(report =>
        {
            if (throttle.ShouldReport(report))
                SetStatus(downloading(report));
        });

        if (whileVerifying is not null && models.HasExpectedFile(model))
            SetStatus(whileVerifying);

        return models.EnsureModelAsync(model, progress, ct);
    }

    private async Task<LoadedModel> LoadAsync(SpeechModelInfo model, string path, HardwareFacts hardware, CancellationToken ct)
    {
        var loadedModel = await loader.LoadAsync(path, hardware.CanUseCuda, hardware.RuntimeDirectory, ct).ConfigureAwait(false);
        return new LoadedModel(model, loadedModel, hardware.AdviceFor(loadedModel.Runtime));
    }

    /// <summary>
    /// Makes <paramref name="next"/> the loaded model once no transcription is using the current one. Disposes
    /// <paramref name="next"/> and returns false when it cannot be swapped in: cancelled, or the engine was disposed.
    /// </summary>
    private async Task<bool> TrySwapInAsync(LoadedModel next, CancellationToken ct)
    {
        try
        {
            await _modelGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            next.Model.Dispose();
            throw;
        }

        try
        {
            if (IsDisposed)
            {
                next.Model.Dispose();
                return false;
            }

            Volatile.Write(ref _loaded, next);
            return true;
        }
        finally
        {
            _modelGate.Release();
        }
    }

    /// <summary>Makes <paramref name="previous"/> the saved choice again, unless a newer choice was made since the switch began.</summary>
    private void RestoreChoice(Operation operation, SpeechModelInfo previous)
    {
        try
        {
            lock (_choiceLock)
            {
                if (_choiceVersion != operation.ChoiceVersion)
                    return;

                settings.Update(s => s with { SpeechModel = previous.Id });
            }

            _logger.Information("Speech model choice restored to {Model}", previous.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not restore {Model} as the speech model", previous.DisplayName);
        }
    }

    /// <summary>
    /// Deletes the files of every catalog model but <paramref name="active"/>, which has just become Ready: the previous
    /// model after a switch, and whatever an unfinished switch or a failed first load left behind. Only from inside an
    /// operation, so no download of another model can be running.
    /// </summary>
    private void DeleteModelsOtherThan(SpeechModelInfo active)
    {
        foreach (var id in Enum.GetValues<SpeechModelId>())
        {
            if (id != active.Id)
                Delete(SpeechModelCatalog.Get(id));
        }
    }

    private void Delete(SpeechModelInfo model)
    {
        try
        {
            models.Delete(model);
            _logger.Debug("Deleted any {Model} files", model.DisplayName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only costs disk space until the next time a model becomes Ready, which tries again; never fails Ready.
            _logger.Warning(ex, "Could not delete the {Model} files", model.DisplayName);
        }
    }

    /// <summary>
    /// The GPU and CUDA runtime facts, gathered once. Looks for the runtime only on a GPU that can use it, and tries CUDA
    /// only where the runtime was found: the locator searches the process PATH first, so this costs nothing in production,
    /// and it keeps Whisper.net from finding a runtime on PATH that the locator was told not to see (the Debug build's
    /// no-CUDA simulation).
    /// </summary>
    private HardwareFacts Hardware()
    {
        if (_hardware is { } known)
            return known;

        var report = gpu.Report;
        var supported = GpuAdvisor.CanUseCuda(report);
        var runtimeDirectory = supported ? cuda.FindRuntimeDirectory() : null;
        var facts = new HardwareFacts(report, supported && runtimeDirectory is not null, runtimeDirectory);
        _logger.Information("CUDA {CanUseCuda}, runtime {RuntimeDirectory}, advice {Advice}",
            supported ? "supported" : "not supported", runtimeDirectory ?? "not found", facts.Advice.State);
        return _hardware = facts;
    }

    private string DescribeStartFailure(Exception ex, SpeechModelInfo? model, bool loading) => ex switch
    {
        InsufficientDiskSpaceException disk =>
            $"Not enough disk space for the speech model: {DescribeShortfall(disk)}. Free some space and restart Murmur.",
        ModelVerificationException => "The speech model download was damaged twice. Restart Murmur to try again.",
        HttpRequestException when !loading =>
            $"Could not download the speech model: {ex.Message} Check the internet connection and restart Murmur.",
        _ when loading && model is not null =>
            $"Could not load the speech model: {ex.Message} If the model file is damaged, delete {models.PathFor(model)} and restart Murmur to download it again.",
        _ => $"Could not {(loading ? "load" : "download")} the speech model: {ex.Message}",
    };

    private static string DescribeSwitchFailure(Exception ex) => ex switch
    {
        InsufficientDiskSpaceException disk => $"not enough disk space. {DescribeShortfall(disk)}.",
        ModelVerificationException => "the download was damaged twice.",
        HttpRequestException => $"{ex.Message} Check the internet connection and try again.",
        _ => ex.Message,
    };

    /// <summary>Raises <see cref="StatusChanged"/> unless nothing changed. Only operations call it, and they never overlap.</summary>
    private void SetStatus(EngineStatus status)
    {
        if (status == Volatile.Read(ref _status))
            return;

        Volatile.Write(ref _status, status);
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "A StatusChanged handler threw while handling {State}", status.State);
        }
    }

    private static void Cancel(Operation operation)
    {
        try
        {
            operation.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended long ago.
        }
    }

    /// <summary>A first load or a switch, running or finished.</summary>
    private sealed class Operation(CancellationTokenSource cts, int choiceVersion)
    {
        private volatile bool _superseded;

        public CancellationTokenSource Cts => cts;

        /// <summary>The choice that started it; a failed switch restores the previous model only while this is the newest.</summary>
        public int ChoiceVersion => choiceVersion;

        public Task Task { get; set; } = Task.CompletedTask;

        /// <summary>The model it ends on; a first load resolves it, so it is null until then.</summary>
        public SpeechModelInfo? Target { get; set; }

        /// <summary>Cancelled by a switch, which reports what comes next.</summary>
        public bool Superseded
        {
            get => _superseded;
            set => _superseded = value;
        }
    }

    private sealed record LoadedModel(SpeechModelInfo Info, ILoadedSpeechModel Model, GpuAdvice Gpu)
    {
        public EngineStatus ReadyStatus => new(EngineState.Ready, Info.DisplayName, Model.Runtime, Gpu: Gpu);
    }

    /// <param name="CanUseCuda">The GPU supports Murmur's CUDA build and the runtime is in <paramref name="RuntimeDirectory"/>.</param>
    private sealed record HardwareFacts(GpuReport Report, bool CanUseCuda, string? RuntimeDirectory)
    {
        /// <summary>The tip before a model is loaded.</summary>
        public GpuAdvice Advice { get; } = GpuAdvisor.Decide(Report, RuntimeDirectory is not null, null);

        public GpuAdvice AdviceFor(string? loadedRuntime) => GpuAdvisor.Decide(Report, RuntimeDirectory is not null, loadedRuntime);
    }

    /// <summary>Unlike <see cref="Progress{T}"/> it runs inline, so reports cannot arrive out of order or after the download.</summary>
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
