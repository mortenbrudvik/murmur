using System.Net;
using Murmur.Core.Services;
using Murmur.Core.Speech;
using Shouldly;

namespace Murmur.Core.Tests.Speech;

public sealed class ModelManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "murmur-models-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ModelManager CreateManager(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new MurmurPaths(_root), new HttpClient(new FakeHandler(respond)));

    private static HttpResponseMessage Response(byte[] body, long contentLength)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentLength = contentLength;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Fact]
    public async Task Downloads_ToPartThenRenames_ReportsProgress()
    {
        var body = new byte[1000];
        Random.Shared.NextBytes(body);
        var manager = CreateManager(_ => Response(body, 1000));
        var progress = new RecordingProgress();

        var path = await manager.EnsureModelAsync(progress, TestContext.Current.CancellationToken);

        path.ShouldBe(manager.ModelPath);
        File.ReadAllBytes(path).ShouldBe(body);
        File.Exists(path + ".part").ShouldBeFalse();
        manager.IsModelPresent.ShouldBeTrue();
        progress.Values.ShouldNotBeEmpty();
        progress.Values.Last().ShouldBe(1.0);
        progress.Values.ShouldBeInOrder(SortDirection.Ascending);
    }

    [Fact]
    public async Task Downloads_FromTheDocumentedUrl()
    {
        Uri? requested = null;
        var manager = CreateManager(request =>
        {
            requested = request.RequestUri;
            return Response(new byte[10], 10);
        });

        await manager.EnsureModelAsync(new RecordingProgress(), TestContext.Current.CancellationToken);

        requested.ShouldBe(new Uri(ModelManager.ModelUrl));
        ModelManager.ModelUrl.ShouldBe("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin");
        Path.GetFileName(manager.ModelPath).ShouldBe(ModelManager.ModelFileName);
    }

    [Fact]
    public async Task ContentLengthMismatch_Throws_LeavesNoFiles()
    {
        var manager = CreateManager(_ => Response(new byte[1000], 2000));

        await Should.ThrowAsync<IOException>(
            () => manager.EnsureModelAsync(new RecordingProgress(), TestContext.Current.CancellationToken));

        File.Exists(manager.ModelPath).ShouldBeFalse();
        File.Exists(manager.ModelPath + ".part").ShouldBeFalse();
        manager.IsModelPresent.ShouldBeFalse();
    }

    [Fact]
    public async Task HttpError_Throws_LeavesNoFiles()
    {
        var manager = CreateManager(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Should.ThrowAsync<HttpRequestException>(
            () => manager.EnsureModelAsync(new RecordingProgress(), TestContext.Current.CancellationToken));

        File.Exists(manager.ModelPath).ShouldBeFalse();
        File.Exists(manager.ModelPath + ".part").ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelled_Throws_LeavesNoFiles()
    {
        var manager = CreateManager(_ => Response(new byte[1000], 1000));
        using var cts = new CancellationTokenSource();
        var progress = new RecordingProgress(onReport: _ => cts.Cancel());

        await Should.ThrowAsync<OperationCanceledException>(
            () => manager.EnsureModelAsync(progress, cts.Token));

        File.Exists(manager.ModelPath).ShouldBeFalse();
        File.Exists(manager.ModelPath + ".part").ShouldBeFalse();
    }

    [Fact]
    public async Task ModelPresent_DoesNotDownload()
    {
        var manager = CreateManager(_ => throw new InvalidOperationException("the network must not be touched"));
        Directory.CreateDirectory(Path.GetDirectoryName(manager.ModelPath)!);
        await File.WriteAllBytesAsync(manager.ModelPath, [1, 2, 3], TestContext.Current.CancellationToken);

        manager.IsModelPresent.ShouldBeTrue();
        var path = await manager.EnsureModelAsync(new RecordingProgress(), TestContext.Current.CancellationToken);

        path.ShouldBe(manager.ModelPath);
    }

    [Fact]
    public void ModelPath_IsInsideTheModelsDirectory()
    {
        var manager = CreateManager(_ => throw new InvalidOperationException());

        manager.ModelPath.ShouldBe(Path.Combine(_root, "models", "ggml-large-v3-turbo.bin"));
        manager.IsModelPresent.ShouldBeFalse();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    /// <summary>Synchronous, unlike <see cref="Progress{T}"/>, so reports are recorded in order and before the call returns.</summary>
    private sealed class RecordingProgress(Action<double>? onReport = null) : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value)
        {
            Values.Add(value);
            onReport?.Invoke(value);
        }
    }
}
