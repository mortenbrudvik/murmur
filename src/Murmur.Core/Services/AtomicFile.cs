namespace Murmur.Core.Services;

/// <summary>
/// Writes a file whole or not at all: to a temp file next to it first, then renamed into place, so a crash cannot leave
/// a half-written file under the real name.
/// </summary>
internal static class AtomicFile
{
    /// <summary>What the temp file adds to the real file's name. A crash can leave one behind.</summary>
    internal const string TempSuffix = ".tmp";

    internal static void WriteAllText(string path, string contents) =>
        Write(path, tempPath => File.WriteAllText(tempPath, contents));

    /// <summary>Creates the folder, has <paramref name="writeTemp"/> write the temp file it is given, and renames it to <paramref name="path"/>.</summary>
    internal static void Write(string path, Action<string> writeTemp)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + TempSuffix;
        writeTemp(tempPath);
        File.Move(tempPath, path, overwrite: true);
    }
}
