using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>Finds the Whisper model on disk and downloads it once, on first run.</summary>
public sealed class ModelManager(MurmurPaths paths, HttpClient http)
{
    public const string ModelFileName = "ggml-large-v3-turbo.bin";
    public const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin";

    private const int BufferSize = 1 << 20;

    private readonly ILogger _logger = Log.ForContext<ModelManager>();

    public string ModelPath { get; } = Path.Combine(paths.ModelsDirectory, ModelFileName);

    public bool IsModelPresent => File.Exists(ModelPath);

    /// <summary>
    /// Returns the model path, downloading the model first when it is missing. The download goes to a
    /// <c>.part</c> file that only becomes the model once its size matches the server's Content-Length,
    /// so a truncated download is never mistaken for a model. Progress is bytes received over Content-Length.
    /// </summary>
    /// <exception cref="HttpRequestException">The server answered with an error status.</exception>
    /// <exception cref="IOException">The download was incomplete, or the server gave no Content-Length to check it against.</exception>
    public async Task<string> EnsureModelAsync(IProgress<double> progress, CancellationToken ct)
    {
        if (IsModelPresent)
            return ModelPath;

        Directory.CreateDirectory(paths.ModelsDirectory);
        var partPath = ModelPath + ".part";
        var completed = false;
        try
        {
            _logger.Information("Downloading speech model from {Url}", ModelUrl);
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var expected = response.Content.Headers.ContentLength
                ?? throw new IOException("The server did not say how large the speech model is, so the download cannot be verified.");

            long received;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                received = await CopyAsync(source, target, expected, progress, ct).ConfigureAwait(false);
            }

            if (received != expected)
                throw new IOException($"The speech model download was incomplete: received {received:N0} of {expected:N0} bytes.");

            File.Move(partPath, ModelPath, overwrite: true);
            completed = true;
            progress.Report(1.0);
            _logger.Information("Speech model saved to {Path} ({Bytes:N0} bytes)", ModelPath, received);
            return ModelPath;
        }
        finally
        {
            if (!completed)
                DeletePartFile(partPath);
        }
    }

    private static async Task<long> CopyAsync(Stream source, Stream target, long expected, IProgress<double> progress, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            progress.Report(expected > 0 ? Math.Min((double)received / expected, 1.0) : 0);
        }

        return received;
    }

    private void DeletePartFile(string partPath)
    {
        try
        {
            File.Delete(partPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "Could not delete the partial download {Path}", partPath);
        }
    }
}
