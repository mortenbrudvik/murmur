using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Murmur.Core.Features.App;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Serilog;

namespace Murmur.UI.About;

/// <summary>
/// What the About window says: which Murmur this is, what it runs on, how much was dictated and where the data is.
/// One instance serves every About window that is opened; <see cref="Refresh"/> brings it up to date for the next one.
/// The model and runtime rows follow the speech engine while a window is open.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private const string CopyDetailsLabel = "Copy details";

    /// <summary>How long "Copied" stays on the button.</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromMilliseconds(1600);

    private readonly ILogger _logger = Log.ForContext<AboutViewModel>();
    private readonly IAppInfo _app;
    private readonly IHistoryStore _history;
    private readonly ISpeechEngine _engine;
    private readonly IMediator _mediator;
    private readonly ITextInserter _inserter;
    private readonly TimeProvider _time;
    private ITimer? _copyLabelReset;

    [ObservableProperty]
    private string _speechModel = "";

    [ObservableProperty]
    private string _runningOn = "";

    [ObservableProperty]
    private string _dictatedMain = "";

    /// <summary>The second line of "Dictated so far"; null when there is nothing to add.</summary>
    [ObservableProperty]
    private string? _dictatedDetail;

    [ObservableProperty]
    private string _copyLabel = CopyDetailsLabel;

    /// <summary>The last link that was asked for could not be opened.</summary>
    [ObservableProperty]
    private bool _linkFailed;

    public AboutViewModel(
        IAppInfo app, IHistoryStore history, ISpeechEngine engine, IMediator mediator, ITextInserter inserter, TimeProvider time)
    {
        _app = app;
        _history = history;
        _engine = engine;
        _mediator = mediator;
        _inserter = inserter;
        _time = time;

        VersionLine = $"Version {app.Version} · {app.Channel}";
        Copyright = $"© {app.BuildYear.ToString(CultureInfo.InvariantCulture)}";
        DataFolder = app.DataFolder;

        Apply(engine.Status);
        // Raised on whichever thread changed the status; WPF takes a property change from any thread.
        engine.StatusChanged += Apply;
    }

    public string VersionLine { get; }

    public string Tagline => AboutText.Tagline;

    /// <summary>"© 2026", in front of the owner's name.</summary>
    public string Copyright { get; }

    public string DataFolder { get; }

    /// <summary>The window asks to be closed: its Close button, or Esc.</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Brings everything up to date for a window that is about to be shown: the engine's status, the history's totals
    /// as they are now, and the button and the link error as on a fresh window.
    /// </summary>
    public void Refresh()
    {
        Apply(_engine.Status);

        try
        {
            (DictatedMain, DictatedDetail) = AboutText.Dictated(_history.Totals(), CultureInfo.CurrentCulture);
        }
        catch (Exception ex)
        {
            // An unreadable history costs its own row, not the window.
            _logger.Warning(ex, "Could not read the history's totals for the About window");
            (DictatedMain, DictatedDetail) = ("Unavailable", null);
        }

        LinkFailed = false;
        _copyLabelReset?.Dispose();
        CopyLabel = CopyDetailsLabel;
    }

    private void Apply(EngineStatus status)
    {
        SpeechModel = AboutText.SpeechModel(status);
        RunningOn = AboutText.RunningOn(status);
    }

    /// <summary>Opens one of Murmur's fixed links, or its data folder. A link that will not open is said so in the window.</summary>
    [RelayCommand]
    private async Task OpenLinkAsync(LinkTarget target)
    {
        LinkFailed = false;
        try
        {
            await _mediator.Send(new OpenLinkRequest(target));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not open {Target} from the About window", target);
            LinkFailed = true;
        }
    }

    /// <summary>Copies four lines for a bug report. Off the UI thread: the copy waits for a paste's clipboard restore.</summary>
    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        var details = AboutText.Details(_app, _engine.Status);
        var copied = await Task.Run(() => _inserter.TryCopy(details));

        CopyLabel = copied ? "Copied" : "Could not copy";
        _copyLabelReset?.Dispose();
        _copyLabelReset = _time.CreateTimer(_ => CopyLabel = CopyDetailsLabel, null, CopiedFor, Timeout.InfiniteTimeSpan);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
