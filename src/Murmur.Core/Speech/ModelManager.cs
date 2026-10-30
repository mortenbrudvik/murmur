using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Murmur.Core.Models;
using Murmur.Core.Services;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>
/// Finds the Whisper model files on disk and downloads them. A download is resumable (HTTP Range, kept across
/// restarts), survives a flaky connection by retrying without end, and counts only once its SHA-256 matches the
/// catalog's pin. A model file that was verified once is trusted again while its size and timestamp are unchanged.
/// </summary>
public sealed class ModelManager(MurmurPaths paths, HttpClient http, TimeProvider time, IDiskSpace disk) : IModelManager
{
    private const int BufferSize = 1 << 20;

    /// <summary>Free space wanted for the bytes still to download, as a percentage: 110 leaves 10 % to spare.</summary>
    private const int DiskHeadroomPercent = 110;

    private static readonly TimeSpan SpeedWindow = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger = Log.ForContext<ModelManager>();
    private readonly JsonFileStore<Dictionary<string, VerifiedFile>> _verified = new(paths.ModelVerificationFile, new Dictionary<string, VerifiedFile>());
    private readonly Lock _verifiedLock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>How long a download may receive no bytes before it is aborted and resumed.</summary>
    public static TimeSpan StallTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>The wait before retry number <paramref name="retry"/> (1 is the first): 2 s, 5 s, 15 s, then 60 s.</summary>
    public static TimeSpan RetryDelay(int retry) => retry switch
    {
        <= 1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(5),
        3 => TimeSpan.FromSeconds(15),
        _ => TimeSpan.FromSeconds(60),
    };

    public string PathFor(SpeechModelInfo model) => Path.Combine(paths.ModelsDirectory, model.FileName);

    public bool HasExpectedFile(SpeechModelInfo model)
    {
        var file = new FileInfo(PathFor(model));
        return file.Exists && file.Length == model.SizeBytes;
    }

    public void Delete(SpeechModelInfo model)
    {
        var modelPath = PathFor(model);
        DeleteIfExists(modelPath);
        DeleteIfExists(modelPath + ".part");
        Forget(model);
    }

    public async Task<string> EnsureModelAsync(SpeechModelInfo model, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        // One download at a time: two writers on the same .part file would corrupt it.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await EnsureCoreAsync(model, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> EnsureCoreAsync(SpeechModelInfo model, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var modelPath = PathFor(model);
        var partPath = modelPath + ".part";
        Directory.CreateDirectory(paths.ModelsDirectory);

        if (File.Exists(modelPath))
        {
            if (await IsVerifiedAsync(model, modelPath, ct).ConfigureAwait(false))
                return modelPath;

            _logger.Warning("The {Model} file {Path} does not match the expected size and SHA-256, so it is downloaded again", model.DisplayName, modelPath);
            File.Delete(modelPath);
            Forget(model);
        }

        for (var attempt = 1; ; attempt++)
        {
            await DownloadPartAsync(model, partPath, progress, ct).ConfigureAwait(false);

            var actual = await HashFileAsync(partPath, ct).ConfigureAwait(false);
            if (SameDigest(actual, model.Sha256))
            {
                File.Move(partPath, modelPath, overwrite: true);
                Remember(model, modelPath);
                _logger.Information("Speech model {Model} saved to {Path} ({Bytes:N0} bytes)", model.DisplayName, modelPath, model.SizeBytes);
                return modelPath;
            }

            File.Delete(partPath);
            if (attempt >= 2)
            {
                throw new ModelVerificationException(
                    $"The downloaded {model.DisplayName} file does not match its expected SHA-256 ({model.Sha256}), even after downloading it twice. Last digest: {actual}.");
            }

            _logger.Warning("The downloaded {Model} file has SHA-256 {Actual} instead of {Expected}; downloading it again from zero",
                model.DisplayName, actual, model.Sha256);
        }
    }

    /// <summary>Whether the model file is the pinned one: by the cache when size and timestamp are unchanged, otherwise by hashing it.</summary>
    private async Task<bool> IsVerifiedAsync(SpeechModelInfo model, string modelPath, CancellationToken ct)
    {
        var file = new FileInfo(modelPath);
        if (file.Length != model.SizeBytes)
            return false;

        if (TryGetVerified(model) is { } entry
            && entry.Size == file.Length
            && entry.LastWriteUtc.Ticks == file.LastWriteTimeUtc.Ticks
            && SameDigest(entry.Sha256, model.Sha256))
        {
            return true;
        }

        _logger.Information("Verifying the {Model} file", model.DisplayName);
        if (!SameDigest(await HashFileAsync(modelPath, ct).ConfigureAwait(false), model.Sha256))
            return false;

        Remember(model, modelPath);
        return true;
    }

    /// <summary>
    /// Downloads until the .part file is as long as the model, resuming and retrying as needed. Does not verify it.
    /// First reports where it starts: the bytes the part already holds, all of them when it is complete.
    /// </summary>
    private async Task DownloadPartAsync(SpeechModelInfo model, string partPath, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var partLength = FileLength(partPath);
        if (partLength > model.SizeBytes)
        {
            _logger.Warning("The partial {Model} download has {Bytes:N0} bytes, more than the model's {Size:N0}, so it is deleted", model.DisplayName, partLength, model.SizeBytes);
            File.Delete(partPath);
            partLength = 0;
        }

        // Before anything that can take a while (the request, or hashing a complete part), so a resumed download is
        // never shown at 0 % and a fresh one shows at once.
        progress.Report(new DownloadProgress(model.DisplayName, partLength, model.SizeBytes, 0, null, 0));
        if (partLength == model.SizeBytes)
            return;

        var required = ((model.SizeBytes - partLength) * DiskHeadroomPercent + 99) / 100;
        var available = disk.GetAvailableFreeSpace(paths.ModelsDirectory);
        if (available < required)
            throw new InsufficientDiskSpaceException(required, available, paths.ModelsDirectory);

        var state = new DownloadState();
        while (true)
        {
            try
            {
                if (await AttemptAsync(model, partPath, state, progress, ct).ConfigureAwait(false))
                    return;
            }
            catch (TransientDownloadException ex)
            {
                state.Retries++;
                var delay = RetryDelay(state.Retries);
                _logger.Warning(ex.InnerException, "The {Model} download was interrupted ({Reason}); retry {Retry} in {Delay:N0} s",
                    model.DisplayName, ex.Message, state.Retries, delay.TotalSeconds);
                progress.Report(new DownloadProgress(model.DisplayName, FileLength(partPath), model.SizeBytes, 0, null, state.Retries));
                await Task.Delay(delay, time, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// One request and as much of its body as arrives. Returns true when the .part file holds the whole model, and
    /// false when the part was thrown away and the next attempt should start at once.
    /// </summary>
    /// <exception cref="TransientDownloadException">Something that is worth retrying went wrong.</exception>
    private async Task<bool> AttemptAsync(SpeechModelInfo model, string partPath, DownloadState state, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var partLength = FileLength(partPath);
        // The header is set on the request itself, so it is sent again to wherever a redirect leads (the CDN).
        using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
        if (partLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(partLength, null);
            _logger.Information("Resuming the {Model} download at {Bytes:N0} bytes", model.DisplayName, partLength);
        }
        else
        {
            _logger.Information("Downloading the {Model} from {Url}", model.DisplayName, model.Url);
        }

        using var response = await SendAsync(request, ct).ConfigureAwait(false);

        var status = response.StatusCode;
        if (status == HttpStatusCode.RequestedRangeNotSatisfiable && partLength > 0)
        {
            _logger.Warning("The server refused to resume the {Model} download at {Bytes:N0} bytes; starting over", model.DisplayName, partLength);
            File.Delete(partPath);
            return false;
        }

        if (IsTransient(status))
            throw new TransientDownloadException($"the server answered {(int)status} {status}");

        response.EnsureSuccessStatusCode();
        if (status is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            throw new HttpRequestException($"The server answered {(int)status} {status}, which is not a download.", null, status);

        // 206 continues the part; 200 means the server ignored the Range header and is sending the whole file.
        var append = status == HttpStatusCode.PartialContent && partLength > 0;
        var received = append ? partLength : 0;
        long attemptBytes = 0;
        var buffer = new byte[BufferSize];

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        using var stall = new CancellationTokenSource(Timeout.InfiniteTimeSpan, time);
        using var readToken = CancellationTokenSource.CreateLinkedTokenSource(ct, stall.Token);
        var meter = new SpeedMeter(time);
        while (received < model.SizeBytes)
        {
            ct.ThrowIfCancellationRequested();
            var read = await ReadAsync(source, buffer.AsMemory(0, (int)Math.Min(buffer.Length, model.SizeBytes - received)), stall, readToken.Token, ct)
                .ConfigureAwait(false);
            if (read == 0)
                throw new TransientDownloadException($"the server ended the download after {received:N0} of {model.SizeBytes:N0} bytes");

            // A failure to write the file is not the network's fault, so it propagates instead of being retried.
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            attemptBytes += read;
            state.Retries = 0;

            var speed = meter.Add(attemptBytes);
            progress.Report(new DownloadProgress(model.DisplayName, received, model.SizeBytes, speed,
                speed > 0 ? (model.SizeBytes - received) / speed : null, 0));
        }

        return true;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            throw new TransientDownloadException(ex.Message, ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsed.
            throw new TransientDownloadException("the server did not answer in time", ex);
        }
    }

    /// <summary>One read that gives up when <see cref="StallTimeout"/> passes without a single byte.</summary>
    private static async Task<int> ReadAsync(Stream source, Memory<byte> buffer, CancellationTokenSource stall, CancellationToken readToken, CancellationToken ct)
    {
        try
        {
            stall.CancelAfter(StallTimeout);
            return await source.ReadAsync(buffer, readToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stall.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TransientDownloadException($"no data arrived for {StallTimeout.TotalSeconds:N0} seconds");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new TransientDownloadException(ex.Message, ex);
        }
        finally
        {
            // Writing to disk is not part of the wait.
            stall.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    private static bool SameDigest(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static long FileLength(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? file.Length : 0;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private VerifiedFile? TryGetVerified(SpeechModelInfo model)
    {
        lock (_verifiedLock)
            return _verified.Load().GetValueOrDefault(model.FileName);
    }

    private void Remember(SpeechModelInfo model, string modelPath)
    {
        var file = new FileInfo(modelPath);
        lock (_verifiedLock)
        {
            // Load returns the shared defaults when the file is missing, so never change what it hands out.
            var entries = new Dictionary<string, VerifiedFile>(_verified.Load())
            {
                [model.FileName] = new VerifiedFile(file.Length, file.LastWriteTimeUtc, model.Sha256),
            };
            _verified.Save(entries);
        }
    }

    private void Forget(SpeechModelInfo model)
    {
        lock (_verifiedLock)
        {
            var entries = new Dictionary<string, VerifiedFile>(_verified.Load());
            if (entries.Remove(model.FileName))
                _verified.Save(entries);
        }
    }

    /// <summary>What the verification cache remembers about a model file; it is trusted while size and timestamp stay the same.</summary>
    private sealed record VerifiedFile(long Size, DateTime LastWriteUtc, string Sha256);

    /// <summary>What the retry loop shares with the attempts: the count of failures in a row.</summary>
    private sealed class DownloadState
    {
        public int Retries { get; set; }
    }

    /// <summary>A failure that is worth another attempt: the connection, the server or a stall, not the disk or a refusal.</summary>
    private sealed class TransientDownloadException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>
    /// Bytes per second over the most recent stretch of at least <see cref="SpeedWindow"/> (so it follows changes
    /// instead of averaging the whole download), measured on the injected clock. Zero until one window has passed.
    /// </summary>
    private sealed class SpeedMeter(TimeProvider time)
    {
        private readonly Queue<(long Timestamp, long Bytes)> _newer = [];
        private (long Timestamp, long Bytes) _baseline = (time.GetTimestamp(), 0);

        public double Add(long bytes)
        {
            var now = time.GetTimestamp();
            _newer.Enqueue((now, bytes));

            // The baseline is the newest sample that is at least a window old; anything older is no longer needed.
            while (_newer.TryPeek(out var next) && time.GetElapsedTime(next.Timestamp, now) >= SpeedWindow)
                _baseline = _newer.Dequeue();

            var elapsed = time.GetElapsedTime(_baseline.Timestamp, now);
            return elapsed >= SpeedWindow ? (bytes - _baseline.Bytes) / elapsed.TotalSeconds : 0;
        }
    }
}
