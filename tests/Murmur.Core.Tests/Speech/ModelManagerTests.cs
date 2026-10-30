using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public sealed class ModelManagerTests : IDisposable
{
    /// <summary>Real time to wait for something that should take microseconds, so a hang fails instead of freezing the run.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "murmur-models-" + Guid.NewGuid().ToString("N"));
    private readonly MurmurPaths _paths;
    private readonly FakeTimeProvider _time = new();
    private readonly IDiskSpace _disk = Substitute.For<IDiskSpace>();
    private readonly RecordingProgress _progress = new();
    private readonly byte[] _body = RandomBytes(1000, seed: 1);
    private readonly SpeechModelInfo _model;
    private FakeHandler _handler = new(_ => throw new InvalidOperationException("no handler"));

    public ModelManagerTests()
    {
        _paths = new MurmurPaths(_root);
        _disk.GetAvailableFreeSpace(Arg.Any<string>()).Returns(long.MaxValue);
        _model = new SpeechModelInfo(SpeechModelId.SmallEn, "Test", "test.bin", _body.Length, Sha256Hex(_body),
            new Uri("https://example.test/test.bin"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string ModelPath => Path.Combine(_root, "models", "test.bin");

    private string PartPath => ModelPath + ".part";

    // ---- the happy path and resuming ------------------------------------------------------------------------

    [Fact]
    public async Task EnsureModelAsync_Missing_DownloadsVerifiesAndRenames()
    {
        var manager = CreateManager(_ => Ok(_body));

        var path = await Ensure(manager);

        path.ShouldBe(ModelPath);
        path.ShouldBe(manager.PathFor(_model));
        File.ReadAllBytes(path).ShouldBe(_body);
        File.Exists(PartPath).ShouldBeFalse();
        var last = _progress.Reports.Last();
        last.BytesReceived.ShouldBe(_body.Length);
        last.TotalBytes.ShouldBe(_body.Length);
        last.ModelName.ShouldBe("Test");
        last.Retries.ShouldBe(0);
        _handler.Ranges.ShouldBe([null]);
    }

    [Fact]
    public async Task EnsureModelAsync_PartFileExists_SendsRangeAndAppends()
    {
        WritePart(_body[..400]);
        var manager = CreateManager(_ => Partial(new ScriptedStream(_body[400..], chunkSize: 100)));

        var path = await Ensure(manager);

        _handler.Ranges.ShouldBe(["bytes=400-"]);
        File.ReadAllBytes(path).ShouldBe(_body);
        File.Exists(PartPath).ShouldBeFalse();
        _progress.Reports[1].BytesReceived.ShouldBe(500, "progress counts the bytes that were already on disk");
    }

    /// <summary>A resumed download starts where its part left off, so the first status never shows 0 %.</summary>
    [Fact]
    public async Task EnsureModelAsync_PartFileExists_FirstReportIsThePartLength()
    {
        WritePart(_body[..400]);
        var manager = CreateManager(_ => Partial(new ScriptedStream(_body[400..], chunkSize: 100)));

        await Ensure(manager);

        _progress.Reports[0].ShouldBe(new DownloadProgress("Test", 400, _body.Length, 0, null, 0));
    }

    [Fact]
    public async Task EnsureModelAsync_Missing_FirstReportIsZeroBeforeAnyRequest()
    {
        DownloadProgress? first = null;
        var calls = -1;
        var progress = new RecordingProgress(report =>
        {
            if (first is null)
            {
                first = report;
                calls = _handler.Calls;
            }
        });
        var manager = CreateManager(_ => Ok(_body));

        await manager.EnsureModelAsync(_model, progress, TestContext.Current.CancellationToken);

        first.ShouldBe(new DownloadProgress("Test", 0, _body.Length, 0, null, 0));
        calls.ShouldBe(0, "the download shows at once, not when the server first answers");
    }

    [Fact]
    public async Task EnsureModelAsync_ServerIgnoresRange_StartsOver()
    {
        WritePart(RandomBytes(400, seed: 2));
        var manager = CreateManager(_ => Ok(new ScriptedStream(_body, chunkSize: 250)));

        var path = await Ensure(manager);

        _handler.Ranges.ShouldBe(["bytes=400-"]);
        File.ReadAllBytes(path).ShouldBe(_body);
        _progress.Reports[0].BytesReceived.ShouldBe(400);
        _progress.Reports[1].BytesReceived.ShouldBe(250, "a 200 answer replaces the part, so counting starts at zero");
    }

    [Fact]
    public async Task EnsureModelAsync_RangeNotSatisfiable_StartsOverWithoutWaiting()
    {
        WritePart(RandomBytes(400, seed: 2));
        var manager = CreateManager(call => call == 1 ? Status(HttpStatusCode.RequestedRangeNotSatisfiable) : Ok(_body));

        var path = await Ensure(manager);

        _handler.Ranges.ShouldBe(["bytes=400-", null]);
        File.ReadAllBytes(path).ShouldBe(_body);
        _progress.Reports.ShouldAllBe(r => r.Retries == 0);
    }

    [Fact]
    public async Task EnsureModelAsync_CompletePartFile_VerifiesWithoutARequest()
    {
        WritePart(_body);
        var manager = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));

        var path = await Ensure(manager);

        _handler.Calls.ShouldBe(0);
        File.ReadAllBytes(path).ShouldBe(_body);
        File.Exists(PartPath).ShouldBeFalse();
    }

    /// <summary>Hashing a complete part takes a moment for the large model; meanwhile the status shows it complete, not 0 %.</summary>
    [Fact]
    public async Task EnsureModelAsync_CompletePartFile_ReportsItCompleteBeforeItIsVerified()
    {
        WritePart(_body);
        var manager = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));

        await Ensure(manager);

        _progress.Reports.ShouldBe([new DownloadProgress("Test", _body.Length, _body.Length, 0, null, 0)]);
    }

    [Fact]
    public async Task EnsureModelAsync_PartLargerThanModel_StartsOver()
    {
        WritePart([.. _body, .. RandomBytes(50, seed: 3)]);
        var manager = CreateManager(_ => Ok(_body));

        var path = await Ensure(manager);

        _handler.Ranges.ShouldBe([null]);
        File.ReadAllBytes(path).ShouldBe(_body);
        _progress.Reports[0].BytesReceived.ShouldBe(0, "the oversized part is gone before the download starts");
    }

    [Fact]
    public async Task EnsureModelAsync_StreamHasMoreThanTheModel_StopsAtTheModelSize()
    {
        var manager = CreateManager(_ => Ok([.. _body, .. RandomBytes(50, seed: 3)]));

        var path = await Ensure(manager);

        File.ReadAllBytes(path).ShouldBe(_body);
    }

    // ---- transient failures ---------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureModelAsync_TransientFailure_RetriesAfterTheBackoff()
    {
        var manager = CreateManager(call => call == 1 ? throw new HttpRequestException("connection refused") : Ok(_body));

        var task = Start(manager);
        await WaitUntilAsync(() => _progress.Reports.Any(r => r.Retries == 1));
        _time.Advance(TimeSpan.FromSeconds(1.9));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _handler.Calls.ShouldBe(1, "the first retry waits 2 seconds");
        await AdvanceUntilAsync(() => _handler.Calls == 2, TimeSpan.FromMilliseconds(100));
        var path = await task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        File.ReadAllBytes(path).ShouldBe(_body);
        var retry = _progress.Reports.First(r => r.Retries == 1);
        retry.BytesReceived.ShouldBe(0);
        retry.BytesPerSecond.ShouldBe(0);
        retry.SecondsRemaining.ShouldBeNull();
        retry.Reconnecting.ShouldBeTrue();
        _progress.Reports.Last().Retries.ShouldBe(0, "bytes flow again");
    }

    [Fact]
    public async Task EnsureModelAsync_ServiceUnavailable_IsRetried()
    {
        var manager = CreateManager(call => call == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Ok(_body));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));
        var path = await task;

        File.ReadAllBytes(path).ShouldBe(_body);
        _handler.Calls.ShouldBe(2);
        _progress.Reports.ShouldContain(r => r.Retries == 1);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task EnsureModelAsync_RetryableStatus_IsRetried(HttpStatusCode status)
    {
        var manager = CreateManager(call => call == 1 ? Status(status) : Ok(_body));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        File.ReadAllBytes(await task).ShouldBe(_body);
        _handler.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task EnsureModelAsync_RequestTimesOut_IsRetried()
    {
        var manager = CreateManager(call => call == 1
            ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException())
            : Ok(_body));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        File.ReadAllBytes(await task).ShouldBe(_body);
        _handler.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task EnsureModelAsync_ConnectionDropsMidStream_ResumesAfterTheBackoff()
    {
        var manager = CreateManager(call => call == 1
            ? Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: (offset, _) =>
                offset >= 300 ? throw new IOException("The connection was reset.") : ValueTask.CompletedTask))
            : Partial(_body[300..]));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        File.ReadAllBytes(await task).ShouldBe(_body);
        _handler.Ranges.ShouldBe([null, "bytes=300-"]);
        _progress.Reports.Single(r => r.Retries == 1).BytesReceived.ShouldBe(300);
    }

    [Fact]
    public async Task EnsureModelAsync_ServerEndsTheBodyEarly_ResumesAfterTheBackoff()
    {
        var manager = CreateManager(call => call == 1 ? Ok(_body[..300]) : Partial(_body[300..]));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        File.ReadAllBytes(await task).ShouldBe(_body);
        _handler.Ranges.ShouldBe([null, "bytes=300-"]);
    }

    [Fact]
    public async Task EnsureModelAsync_RetriesAreConsecutive_CountFromZeroOnceBytesFlow()
    {
        // fails without bytes, fails after 100 bytes, fails without bytes, then succeeds
        var manager = CreateManager(call => call switch
        {
            1 or 3 => throw new HttpRequestException("connection refused"),
            2 => Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: (offset, _) =>
                offset >= 100 ? throw new IOException("The connection was reset.") : ValueTask.CompletedTask)),
            _ => Partial(_body[100..]),
        });

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        File.ReadAllBytes(await task).ShouldBe(_body);
        _progress.Reports.Where(r => r.Retries > 0).Select(r => r.Retries).ShouldBe([1, 1, 2]);
    }

    [Fact]
    public async Task EnsureModelAsync_NotFound_ThrowsWithoutRetrying()
    {
        var manager = CreateManager(_ => Status(HttpStatusCode.NotFound));

        var ex = await Should.ThrowAsync<HttpRequestException>(() => Start(manager));

        ex.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _handler.Calls.ShouldBe(1);
        _progress.Reports.ShouldAllBe(r => r.Retries == 0);
        File.Exists(ModelPath).ShouldBeFalse();
        File.Exists(PartPath).ShouldBeFalse();
    }

    [Fact]
    public async Task EnsureModelAsync_WritingThePartFileFails_ThrowsWithoutRetrying()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PartPath)!);
        await using var held = new FileStream(PartPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await held.WriteAsync(_body.AsMemory(0, 400), TestContext.Current.CancellationToken);
        await held.FlushAsync(TestContext.Current.CancellationToken);
        var manager = CreateManager(_ => Partial(_body[400..]));

        var ex = await Should.ThrowAsync<IOException>(() => Start(manager));

        ex.ShouldNotBeOfType<InsufficientDiskSpaceException>();
        _handler.Calls.ShouldBe(1);
        _progress.Reports.ShouldAllBe(r => r.Retries == 0);
    }

    // ---- secure-connection failures -------------------------------------------------------------------------

    /// <summary>A proxy that inspects HTTPS with a certificate Windows does not trust fails every handshake.</summary>
    [Fact]
    public async Task EnsureModelAsync_TlsFailure_FailsAfterThreeRetries()
    {
        var manager = CreateManager(_ => throw TlsFailure());

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        var ex = await Should.ThrowAsync<HttpRequestException>(() => task);
        ex.HttpRequestError.ShouldBe(HttpRequestError.SecureConnectionError);
        ex.InnerException.ShouldBeOfType<AuthenticationException>();
        _handler.Calls.ShouldBe(4);
        _progress.Reports.Where(r => r.Retries > 0).Select(r => r.Retries).ShouldBe([1, 2, 3]);
        File.Exists(ModelPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    [InlineData(HttpRequestError.UserAuthenticationError)]
    public async Task EnsureModelAsync_ProxyFailure_FailsAfterThreeRetries(HttpRequestError error)
    {
        var manager = CreateManager(_ => throw new HttpRequestException(error, "The proxy tunnel request to proxy 'http://proxy.test/' failed with status code '407'."));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        (await Should.ThrowAsync<HttpRequestException>(() => task)).HttpRequestError.ShouldBe(error);
        _handler.Calls.ShouldBe(4);
    }

    /// <summary>A captive portal breaks TLS only until the user has signed in to it.</summary>
    [Fact]
    public async Task EnsureModelAsync_TlsFailureThenSuccess_Downloads()
    {
        var manager = CreateManager(call => call <= 2 ? throw TlsFailure() : Ok(_body));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        File.ReadAllBytes(await task).ShouldBe(_body);
        _handler.Calls.ShouldBe(3);
        _progress.Reports.Where(r => r.Retries > 0).Select(r => r.Retries).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task EnsureModelAsync_TlsFailureMidBody_FailsAfterThreeRetries()
    {
        var manager = CreateManager(_ => Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: (_, _) => throw TlsFailure())));

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        (await Should.ThrowAsync<HttpRequestException>(() => task)).HttpRequestError.ShouldBe(HttpRequestError.SecureConnectionError);
        _handler.Calls.ShouldBe(4);
    }

    /// <summary>Only failures in a row count: bytes arriving between them prove the connection works.</summary>
    [Fact]
    public async Task EnsureModelAsync_TlsFailuresWithBytesBetween_KeepRetrying()
    {
        var manager = CreateManager(call => call switch
        {
            <= 3 or 5 or 6 => throw TlsFailure(),
            4 => Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: (offset, _) =>
                offset >= 100 ? throw TlsFailure() : ValueTask.CompletedTask)),
            _ => Partial(_body[100..]),
        });

        var task = Start(manager);
        await AdvanceUntilAsync(() => task.IsCompleted, TimeSpan.FromSeconds(1));

        File.ReadAllBytes(await task).ShouldBe(_body);
        _handler.Calls.ShouldBe(7);
    }

    // ---- stalls ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureModelAsync_NoBytesFor30Seconds_AbortsAndResumes()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = CreateManager(call => call == 1
            ? Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: async (offset, token) =>
            {
                if (offset < 100)
                    return;

                waiting.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }))
            : Partial(_body[100..]));

        var task = Start(manager);
        await waiting.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _progress.Reports.ShouldAllBe(r => r.Retries == 0, "29 seconds of silence is not a stall yet");

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => _progress.Reports.Any(r => r.Retries == 1));
        await AdvanceUntilAsync(() => _handler.Calls == 2, TimeSpan.FromSeconds(1));
        var path = await task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        _handler.Ranges.ShouldBe([null, "bytes=100-"]);
        File.ReadAllBytes(path).ShouldBe(_body);
    }

    [Fact]
    public void StallTimeout_IsThirtySeconds() => ModelManager.StallTimeout.ShouldBe(TimeSpan.FromSeconds(30));

    // ---- disk space -----------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureModelAsync_NotEnoughDiskSpace_Throws()
    {
        _disk.GetAvailableFreeSpace(Arg.Any<string>()).Returns(500L);
        var manager = CreateManager(_ => Ok(_body));

        var ex = await Should.ThrowAsync<InsufficientDiskSpaceException>(() => Start(manager));

        ex.RequiredBytes.ShouldBe(1100);
        ex.AvailableBytes.ShouldBe(500);
        _handler.Calls.ShouldBe(0);
        _disk.Received().GetAvailableFreeSpace(Path.Combine(_root, "models"));
    }

    [Fact]
    public async Task EnsureModelAsync_NotEnoughDiskSpaceForTheRest_CountsOnlyWhatIsMissing()
    {
        WritePart(_body[..395]);
        _disk.GetAvailableFreeSpace(Arg.Any<string>()).Returns(665L);
        var manager = CreateManager(_ => Partial(_body[395..]));

        var ex = await Should.ThrowAsync<InsufficientDiskSpaceException>(() => Start(manager));

        ex.RequiredBytes.ShouldBe(666, "605 bytes missing times 1.1 is 665.5, rounded up");
        _handler.Calls.ShouldBe(0);
        File.Exists(PartPath).ShouldBeTrue("the part is kept for when there is room");

        _disk.GetAvailableFreeSpace(Arg.Any<string>()).Returns(666L);
        File.ReadAllBytes(await Ensure(manager)).ShouldBe(_body);
    }

    // ---- verification ---------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureModelAsync_ChecksumMismatchOnce_DownloadsAgain()
    {
        var manager = CreateManager(call => Ok(call == 1 ? RandomBytes(_body.Length, seed: 9) : _body));

        var path = await Ensure(manager);

        File.ReadAllBytes(path).ShouldBe(_body);
        _handler.Ranges.ShouldBe([null, null], "the corrupt part is deleted, so the second download starts from zero");
        File.Exists(PartPath).ShouldBeFalse();
    }

    [Fact]
    public async Task EnsureModelAsync_ChecksumMismatchTwice_Throws()
    {
        var manager = CreateManager(_ => Ok(RandomBytes(_body.Length, seed: 9)));

        await Should.ThrowAsync<ModelVerificationException>(() => Start(manager));

        _handler.Calls.ShouldBe(2);
        File.Exists(ModelPath).ShouldBeFalse();
        File.Exists(PartPath).ShouldBeFalse();
    }

    [Fact]
    public async Task EnsureModelAsync_CorruptCompletePart_IsDeletedAndDownloadedAgain()
    {
        WritePart(RandomBytes(_body.Length, seed: 9));
        var manager = CreateManager(_ => Ok(_body));

        var path = await Ensure(manager);

        _handler.Ranges.ShouldBe([null]);
        File.ReadAllBytes(path).ShouldBe(_body);
    }

    [Fact]
    public async Task EnsureModelAsync_PresentFileWithoutCacheEntry_IsHashedAcceptedAndRemembered()
    {
        WriteModel(_body);
        var manager = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));

        var path = await Ensure(manager);

        path.ShouldBe(ModelPath);
        _handler.Calls.ShouldBe(0);
        File.ReadAllText(_paths.ModelVerificationFile).ShouldContain("test.bin");
        _progress.Reports.ShouldBeEmpty("nothing was downloaded");
    }

    [Fact]
    public async Task EnsureModelAsync_PresentFileWithWrongContent_IsReplaced()
    {
        WriteModel(RandomBytes(_body.Length, seed: 9));
        var manager = CreateManager(_ => Ok(_body));

        var path = await Ensure(manager);

        _handler.Ranges.ShouldBe([null], "a bad model file is never resumed from");
        File.ReadAllBytes(path).ShouldBe(_body);
    }

    [Fact]
    public async Task EnsureModelAsync_PresentFileWithWrongSize_IsReplaced()
    {
        WriteModel(_body[..10]);
        var manager = CreateManager(_ => Ok(_body));

        var path = await Ensure(manager);

        _handler.Ranges.ShouldBe([null]);
        File.ReadAllBytes(path).ShouldBe(_body);
    }

    [Fact]
    public async Task EnsureModelAsync_VerifiedEarlier_DoesNotRehash()
    {
        var path = await Ensure(CreateManager(_ => Ok(_body)));
        var stamp = File.GetLastWriteTimeUtc(path);
        var garbage = RandomBytes(_body.Length, seed: 9);
        File.WriteAllBytes(path, garbage);
        File.SetLastWriteTimeUtc(path, stamp);
        var second = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));

        var again = await Ensure(second);

        again.ShouldBe(path);
        _handler.Calls.ShouldBe(0);
        File.ReadAllBytes(path).ShouldBe(garbage, "the cache said it was verified, so nothing was read");
    }

    [Fact]
    public async Task EnsureModelAsync_FileChangedSinceVerified_RehashesAndRedownloads()
    {
        var path = await Ensure(CreateManager(_ => Ok(_body)));
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, RandomBytes(_body.Length, seed: 9));
        File.SetLastWriteTimeUtc(path, stamp.AddMinutes(1));
        var second = CreateManager(_ => Ok(_body));

        var again = await Ensure(second);

        again.ShouldBe(path);
        _handler.Calls.ShouldBe(1);
        File.ReadAllBytes(path).ShouldBe(_body);
    }

    [Fact]
    public async Task VerifyAsync_FileMatches_IsTrueAndRemembered()
    {
        WriteModel(_body);
        var manager = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));

        (await manager.VerifyAsync(_model, TestContext.Current.CancellationToken)).ShouldBeTrue();

        _handler.Calls.ShouldBe(0);
        File.ReadAllText(_paths.ModelVerificationFile).ShouldContain("test.bin");
    }

    [Fact]
    public async Task VerifyAsync_FileWithWrongContent_IsFalseAndNeitherDownloadsNorDeletes()
    {
        var garbage = RandomBytes(_body.Length, seed: 9);
        WriteModel(garbage);
        var manager = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));

        (await manager.VerifyAsync(_model, TestContext.Current.CancellationToken)).ShouldBeFalse();

        _handler.Calls.ShouldBe(0);
        File.ReadAllBytes(ModelPath).ShouldBe(garbage);
    }

    [Fact]
    public async Task VerifyAsync_Missing_IsFalse()
    {
        WritePart(_body);
        var manager = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));

        (await manager.VerifyAsync(_model, TestContext.Current.CancellationToken)).ShouldBeFalse();

        _handler.Calls.ShouldBe(0);
        File.Exists(PartPath).ShouldBeTrue("a complete partial download is not the model file");
    }

    // ---- cancellation and progress --------------------------------------------------------------------------

    [Fact]
    public async Task EnsureModelAsync_Cancelled_KeepsThePartFile()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new RecordingProgress(report =>
        {
            if (report.BytesReceived >= 200)
                cts.Cancel();
        });
        var manager = CreateManager(_ => Ok(new ScriptedStream(_body, chunkSize: 100)));

        await Should.ThrowAsync<OperationCanceledException>(() => manager.EnsureModelAsync(_model, progress, cts.Token));

        File.Exists(ModelPath).ShouldBeFalse();
        File.Exists(PartPath).ShouldBeTrue();
        new FileInfo(PartPath).Length.ShouldBe(200);
    }

    [Fact]
    public async Task EnsureModelAsync_CancelledWhileWaitingToRetry_KeepsThePartFile()
    {
        WritePart(_body[..400]);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var manager = CreateManager(_ => throw new HttpRequestException("connection refused"));

        var task = manager.EnsureModelAsync(_model, _progress, cts.Token);
        await WaitUntilAsync(() => _progress.Reports.Any(r => r.Retries == 1));
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => task);
        new FileInfo(PartPath).Length.ShouldBe(400);
    }

    [Fact]
    public async Task EnsureModelAsync_Progress_CarriesSpeedAndEta()
    {
        var manager = CreateManager(_ => Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: AdvanceOneSecond)));

        await Ensure(manager);

        // After the report of where the download starts, one per write.
        _progress.Reports[0].ShouldBe(new DownloadProgress("Test", 0, 1000, 0, null, 0));
        var reports = _progress.Reports[1..];
        reports.Count.ShouldBe(10);
        reports[0].BytesReceived.ShouldBe(100);
        reports[0].BytesPerSecond.ShouldBe(100, 1e-9);
        reports[0].SecondsRemaining.ShouldNotBeNull().ShouldBe(9, 1e-9);
        reports[4].BytesReceived.ShouldBe(500);
        reports[4].BytesPerSecond.ShouldBe(100, 1e-9);
        reports[4].SecondsRemaining.ShouldNotBeNull().ShouldBe(5, 1e-9);
        reports[9].SecondsRemaining.ShouldNotBeNull().ShouldBe(0, 1e-9);
        reports.ShouldAllBe(r => r.Retries == 0 && r.TotalBytes == 1000);
    }

    [Fact]
    public async Task EnsureModelAsync_Progress_HasNoSpeedDuringTheFirstSecond()
    {
        var manager = CreateManager(_ => Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: (_, _) =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(500));
            return ValueTask.CompletedTask;
        })));

        await Ensure(manager);

        var reports = _progress.Reports[1..];
        reports[0].BytesPerSecond.ShouldBe(0);
        reports[0].SecondsRemaining.ShouldBeNull();
        reports[1].BytesPerSecond.ShouldBe(200, 1e-9);
        reports[1].SecondsRemaining.ShouldNotBeNull().ShouldBe(4, 1e-9);
    }

    [Fact]
    public async Task EnsureModelAsync_Progress_SpeedFollowsTheLastSecond()
    {
        // 100 bytes per second for 5 s, then 400 bytes per second: the average must move on from the slow start
        var manager = CreateManager(_ => Ok(new ScriptedStream(_body, chunkSize: 100, beforeRead: (offset, _) =>
        {
            _time.Advance(offset < 500 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(250));
            return ValueTask.CompletedTask;
        })));

        await Ensure(manager);

        var reports = _progress.Reports[1..];
        reports[4].BytesPerSecond.ShouldBe(100, 1e-9);
        reports[9].BytesPerSecond.ShouldBe(400, 1e-9);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(20, 60)]
    public void RetryDelay_FollowsTheBackoff(int retry, int seconds) =>
        ModelManager.RetryDelay(retry).ShouldBe(TimeSpan.FromSeconds(seconds));

    // ---- files ----------------------------------------------------------------------------------------------

    [Fact]
    public void HasExpectedFile_WrongSize_IsFalse()
    {
        var manager = CreateManager(_ => throw new InvalidOperationException());
        manager.HasExpectedFile(_model).ShouldBeFalse("missing");

        WriteModel(_body[..999]);

        manager.HasExpectedFile(_model).ShouldBeFalse("one byte short");
    }

    [Fact]
    public void HasExpectedFile_RightSize_IsTrueWithoutHashing()
    {
        var manager = CreateManager(_ => throw new InvalidOperationException());
        WriteModel(RandomBytes(_body.Length, seed: 9));

        manager.HasExpectedFile(_model).ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_RemovesModelPartAndCacheEntry()
    {
        var manager = CreateManager(_ => Ok(_body));
        await Ensure(manager);
        WritePart([1, 2, 3]);
        File.ReadAllText(_paths.ModelVerificationFile).ShouldContain("test.bin");

        manager.Delete(_model);

        File.Exists(ModelPath).ShouldBeFalse();
        File.Exists(PartPath).ShouldBeFalse();
        File.ReadAllText(_paths.ModelVerificationFile).ShouldNotContain("test.bin");
        manager.HasExpectedFile(_model).ShouldBeFalse();
    }

    [Fact]
    public void Delete_NothingOnDisk_DoesNotThrow()
    {
        var manager = CreateManager(_ => throw new InvalidOperationException());

        Should.NotThrow(() => manager.Delete(_model));
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    private ModelManager CreateManager(Func<int, HttpResponseMessage> respond)
    {
        _handler = new FakeHandler(respond);
        return new ModelManager(_paths, new HttpClient(_handler), _time, _disk);
    }

    private Task<string> Start(ModelManager manager) =>
        manager.EnsureModelAsync(_model, _progress, TestContext.Current.CancellationToken);

    private Task<string> Ensure(ModelManager manager) =>
        Start(manager).WaitAsync(Patience, TestContext.Current.CancellationToken);

    private void WritePart(byte[] bytes) => Write(PartPath, bytes);

    private void WriteModel(byte[] bytes) => Write(ModelPath, bytes);

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private ValueTask AdvanceOneSecond(int offset, CancellationToken token)
    {
        _time.Advance(TimeSpan.FromSeconds(1));
        return ValueTask.CompletedTask;
    }

    private static byte[] RandomBytes(int count, int seed)
    {
        var bytes = new byte[count];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Ok(Stream body) => new(HttpStatusCode.OK) { Content = new StreamContent(body) };

    private static HttpResponseMessage Partial(byte[] body) =>
        new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Partial(Stream body) =>
        new(HttpStatusCode.PartialContent) { Content = new StreamContent(body) };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    /// <summary>What a TLS handshake against an untrusted root certificate throws.</summary>
    private static HttpRequestException TlsFailure() => new(HttpRequestError.SecureConnectionError,
        "The SSL connection could not be established, see inner exception.",
        new AuthenticationException("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot"));

    /// <summary>Polls in real time for something another thread is about to do.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > Patience)
                throw new TimeoutException("The condition was not met in time.");

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Moves the fake clock forward in steps until the condition holds. Stepping rather than one big jump means the
    /// manager cannot miss the advance by creating its timer a moment too late.
    /// </summary>
    private async Task AdvanceUntilAsync(Func<bool> condition, TimeSpan step)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > Patience)
                throw new TimeoutException("The condition was not met in time.");

            _time.Advance(step);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Records each request's Range header and answers with <c>respond(callNumber)</c>, counting from 1.</summary>
    private sealed class FakeHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<string?> _ranges = [];

        public int Calls
        {
            get
            {
                lock (_ranges)
                    return _ranges.Count;
            }
        }

        public IReadOnlyList<string?> Ranges
        {
            get
            {
                lock (_ranges)
                    return [.. _ranges];
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int call;
            lock (_ranges)
            {
                _ranges.Add(request.Headers.Range?.ToString());
                call = _ranges.Count;
            }

            try
            {
                return Task.FromResult(respond(call));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }

    /// <summary>Synchronous and thread-safe, so reports are recorded in order and before the call returns.</summary>
    private sealed class RecordingProgress(Action<DownloadProgress>? onReport = null) : IProgress<DownloadProgress>
    {
        private readonly List<DownloadProgress> _reports = [];

        public List<DownloadProgress> Reports
        {
            get
            {
                lock (_reports)
                    return [.. _reports];
            }
        }

        public void Report(DownloadProgress value)
        {
            lock (_reports)
                _reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    /// <summary>
    /// A response body that hands out at most <c>chunkSize</c> bytes per read, so a download makes several reads.
    /// <c>beforeRead</c> runs before each read with the offset about to be read; it can advance the fake clock,
    /// throw, or wait on the token like a connection that has gone quiet.
    /// </summary>
    private sealed class ScriptedStream(byte[] data, int chunkSize, Func<int, CancellationToken, ValueTask>? beforeRead = null) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (beforeRead is not null)
                await beforeRead(_position, cancellationToken);

            var count = Math.Min(Math.Min(chunkSize, buffer.Length), data.Length - _position);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
