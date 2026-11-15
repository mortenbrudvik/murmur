using System.IO;
using MediatR;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Features.App;
using Murmur.Core.Insertion;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Murmur.UI.About;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Murmur.UI.Tests.About;

public sealed class AboutViewModelTests
{
    private static readonly EngineStatus Ready = new(
        EngineState.Ready, "Whisper large-v3-turbo", "Cuda", Gpu: new GpuAdvice(GpuAdviceState.CudaInUse, "NVIDIA GeForce RTX 3090"));

    private readonly IAppInfo _app = Substitute.For<IAppInfo>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly ISpeechEngine _engine = Substitute.For<ISpeechEngine>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITextInserter _inserter = Substitute.For<ITextInserter>();
    private readonly FakeTimeProvider _time = new();

    public AboutViewModelTests()
    {
        _app.Version.Returns("1.2.0");
        _app.Channel.Returns("Built from source");
        _app.DataFolder.Returns(@"C:\Users\me\AppData\Local\Murmur");
        _app.WindowsBuild.Returns("Windows 11, build 26200");
        _app.BuildYear.Returns(2026);
        _history.Totals().Returns(new HistoryTotals(11, 64, new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero)));
        _engine.Status.Returns(Ready);
        _inserter.TryCopy(Arg.Any<string>()).Returns(true);
    }

    private AboutViewModel NewModel() => new(_app, _history, _engine, _mediator, _inserter, _time);

    [Fact]
    public void NewOne_SaysWhatThisCopyIs()
    {
        var model = NewModel();

        model.VersionLine.ShouldBe("Version 1.2.0 · Built from source");
        model.Tagline.ShouldBe("Hold a key, talk, and the words land where you were typing. Nothing you say leaves this PC.");
        model.Copyright.ShouldBe("© 2026");
        model.DataFolder.ShouldBe(@"C:\Users\me\AppData\Local\Murmur");
        model.CopyLabel.ShouldBe("Copy details");
        model.LinkFailed.ShouldBeFalse();
    }

    [Fact]
    public void Refresh_ShowsTheModelTheRuntimeAndWhatWasDictated()
    {
        var model = NewModel();

        model.Refresh();

        model.SpeechModel.ShouldBe("Whisper large-v3-turbo");
        model.RunningOn.ShouldBe("NVIDIA GeForce RTX 3090 · CUDA");
        model.DictatedMain.ShouldBe(64.ToString("N0") + " words");
        model.DictatedDetail.ShouldBe("in 11 dictations since October 2026");
    }

    [Fact]
    public void Refresh_EmptyHistory_SaysNothingYetWithNoSecondLine()
    {
        _history.Totals().Returns(new HistoryTotals(0, 0, null));
        var model = NewModel();

        model.Refresh();

        model.DictatedMain.ShouldBe("Nothing yet");
        model.DictatedDetail.ShouldBeNull();
    }

    /// <summary>A history file that cannot be read costs its own row, not the window.</summary>
    [Fact]
    public void Refresh_HistoryCannotBeRead_SaysUnavailableAndShowsTheRest()
    {
        _history.Totals().Throws(new IOException("locked"));
        var model = NewModel();

        Should.NotThrow(model.Refresh);

        model.DictatedMain.ShouldBe("Unavailable");
        model.DictatedDetail.ShouldBeNull();
        model.SpeechModel.ShouldBe("Whisper large-v3-turbo");
    }

    /// <summary>The count is read each time the window opens, so it is the count of that moment.</summary>
    [Fact]
    public void Refresh_Again_ReadsTheHistoryAgain()
    {
        var model = NewModel();
        model.Refresh();
        _history.Totals().Returns(new HistoryTotals(12, 70, new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero)));

        model.Refresh();

        model.DictatedMain.ShouldBe("70 words");
        model.DictatedDetail.ShouldBe("in 12 dictations since October 2026");
    }

    [Fact]
    public void EngineStatusChanges_AreFollowed()
    {
        var model = NewModel();
        model.Refresh();

        _engine.StatusChanged += Raise.Event<Action<EngineStatus>>(new EngineStatus(EngineState.Loading, "Whisper small (English)"));

        model.SpeechModel.ShouldBe("Whisper small (English)");
        model.RunningOn.ShouldBe("Starting…");
    }

    [Fact]
    public void EngineHasNoModelYet_SaysNotChosenYet()
    {
        _engine.Status.Returns(new EngineStatus(EngineState.NotStarted, ""));
        var model = NewModel();

        model.Refresh();

        model.SpeechModel.ShouldBe("Not chosen yet");
        model.RunningOn.ShouldBe("Starting…");
    }

    [Theory]
    [InlineData(LinkTarget.Repository)]
    [InlineData(LinkTarget.PrivacyPolicy)]
    [InlineData(LinkTarget.License)]
    [InlineData(LinkTarget.HomePage)]
    [InlineData(LinkTarget.DataFolder)]
    public async Task OpenLink_AsksForThatLink(LinkTarget target)
    {
        var model = NewModel();

        await model.OpenLinkCommand.ExecuteAsync(target);

        await _mediator.Received(1).Send(new OpenLinkRequest(target), Arg.Any<CancellationToken>());
        model.LinkFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task OpenLink_CannotBeOpened_SaysSoUntilOneOpens()
    {
        var model = NewModel();
        _mediator.Send(Arg.Any<OpenLinkRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no browser")), Task.CompletedTask);

        await model.OpenLinkCommand.ExecuteAsync(LinkTarget.Repository);
        model.LinkFailed.ShouldBeTrue();

        await model.OpenLinkCommand.ExecuteAsync(LinkTarget.License);
        model.LinkFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task CopyDetails_PutsTheFourLinesOnTheClipboard_AndSaysCopiedForAWhile()
    {
        var model = NewModel();
        model.Refresh();

        await model.CopyDetailsCommand.ExecuteAsync(null);

        _inserter.Received(1).TryCopy(string.Join(
            "\n",
            "Murmur 1.2.0 (Built from source)",
            "Model: Whisper large-v3-turbo",
            "Runtime: CUDA, NVIDIA GeForce RTX 3090",
            "Windows 11, build 26200"));
        model.CopyLabel.ShouldBe("Copied");

        _time.Advance(TimeSpan.FromMilliseconds(1500));
        model.CopyLabel.ShouldBe("Copied");
        _time.Advance(TimeSpan.FromMilliseconds(200));
        model.CopyLabel.ShouldBe("Copy details");
    }

    [Fact]
    public async Task CopyDetails_ClipboardRefuses_SaysCouldNotCopy()
    {
        _inserter.TryCopy(Arg.Any<string>()).Returns(false);
        var model = NewModel();

        await model.CopyDetailsCommand.ExecuteAsync(null);

        model.CopyLabel.ShouldBe("Could not copy");
    }

    [Fact]
    public async Task Refresh_PutsTheButtonAndTheLinkErrorBackAsTheyWere()
    {
        var model = NewModel();
        _mediator.Send(Arg.Any<OpenLinkRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no browser")));
        await model.OpenLinkCommand.ExecuteAsync(LinkTarget.Repository);
        await model.CopyDetailsCommand.ExecuteAsync(null);

        model.Refresh();

        model.LinkFailed.ShouldBeFalse();
        model.CopyLabel.ShouldBe("Copy details");
    }

    [Fact]
    public void Close_AsksTheWindowToClose()
    {
        var model = NewModel();
        var asked = 0;
        model.CloseRequested += () => asked++;

        model.CloseCommand.Execute(null);

        asked.ShouldBe(1);
    }
}
