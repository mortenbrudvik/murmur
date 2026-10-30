using Murmur.Core.Models;

namespace Murmur.Core.Speech;

/// <summary>Finds, downloads and verifies the Whisper model files.</summary>
public interface IModelManager
{
    string PathFor(SpeechModelInfo model);

    /// <summary>True when the model file exists with the catalog size. Reads no file content, so it says nothing about integrity.</summary>
    bool HasExpectedFile(SpeechModelInfo model);

    /// <summary>
    /// Returns the path of a verified model file, resuming, retrying and downloading as needed. Transient network
    /// failures are retried without end (progress reports them), except that a secure connection that fails
    /// <see cref="ModelManager.SecureConnectionRetries"/> retries in a row fails the download; cancelling keeps the
    /// partial download for the next start. A download first reports, at once, where it starts: the bytes its partial
    /// download already holds (all of them for a complete one that is about to be verified). A model file that is
    /// already there and verifies reports nothing.
    /// </summary>
    /// <exception cref="HttpRequestException">
    /// The server answered with an error that retrying will not fix, or no secure connection could be made to it
    /// (<see cref="HttpRequestException.HttpRequestError"/> says which).
    /// </exception>
    /// <exception cref="HttpIOException">
    /// No secure connection could be made, and the last of the secure-connection failures counted towards giving up came
    /// while the body was read (<see cref="HttpIOException.HttpRequestError"/> is
    /// <see cref="HttpRequestError.SecureConnectionError"/>).
    /// </exception>
    /// <exception cref="InsufficientDiskSpaceException">The drive cannot hold the rest of the download.</exception>
    /// <exception cref="ModelVerificationException">Two downloads in a row did not match the pinned SHA-256.</exception>
    Task<string> EnsureModelAsync(SpeechModelInfo model, IProgress<DownloadProgress> progress, CancellationToken ct);

    /// <summary>
    /// Whether the model file is there and matches its pinned SHA-256, trusting the verification cache the way
    /// <see cref="EnsureModelAsync"/> does. Unlike it, never downloads and never deletes anything.
    /// </summary>
    Task<bool> VerifyAsync(SpeechModelInfo model, CancellationToken ct);

    /// <summary>Removes the model file, its partial download and its verification entry. A missing file is not an error.</summary>
    void Delete(SpeechModelInfo model);
}
