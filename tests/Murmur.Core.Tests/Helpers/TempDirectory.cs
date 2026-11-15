namespace Murmur.Core.Tests.Helpers;

/// <summary>
/// A folder of its own under %TEMP% for one test or test class, deleted with everything in it on dispose. It is only
/// named here: whatever the test does creates it, so a test can also check that something does.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "murmur-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
