using System.Text.RegularExpressions;
using Microsoft.Win32;
using Serilog;

namespace Murmur.Core.Speech;

/// <summary>
/// Where to look for the CUDA 13 runtime, read from the system so a test can supply its own.
/// </summary>
/// <param name="ProcessPath">This process's PATH, as it was when the process started.</param>
/// <param name="MachinePath">The machine Path from the registry: what a process started from now on would get.</param>
/// <param name="UserPath">The user Path from the registry, likewise.</param>
/// <param name="CudaPath">The CUDA_PATH of the process, or of the machine when the process has none.</param>
/// <param name="ProgramFiles">The 64-bit Program Files folder, where the CUDA toolkit installs itself.</param>
/// <param name="ListDirectories">The directories directly inside a folder; may throw when the folder is unreadable.</param>
internal sealed record CudaSearchSources(
    string? ProcessPath,
    string? MachinePath,
    string? UserPath,
    string? CudaPath,
    string? ProgramFiles,
    Func<string, IEnumerable<string>> ListDirectories);

/// <summary>
/// The directories that may hold the CUDA 13 runtime DLLs, most trustworthy first. Beyond the process PATH it reads the
/// registry's machine and user Path and the toolkit's own install folder, so an install made while Murmur was running
/// is found at the next start: a running process never sees the PATH change, and neither does the Microsoft Store
/// build, whose DLL search ignores PATH altogether (see <see cref="CudaRuntimeLoader"/>).
/// </summary>
internal static partial class CudaRuntimeSearch
{
    private const string MachineEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string UserEnvironmentKey = "Environment";
    private const string ToolkitMajorFolder = "v13.";

    private static readonly ILogger Logger = Log.ForContext(typeof(CudaRuntimeSearch));

    /// <summary>
    /// The candidate directories in search order: the process PATH, the machine Path, the user Path, CUDA_PATH's
    /// <c>bin\x64</c> then <c>bin</c>, then each <c>v13.*</c> toolkit folder under Program Files, newest first, each as
    /// <c>bin\x64</c> then <c>bin</c>. Entries that are not fully qualified are dropped, and so are repeats (ignoring
    /// case and a trailing separator), keeping the first.
    /// </summary>
    internal static IReadOnlyList<string> Directories(CudaSearchSources sources)
    {
        var directories = new List<string>();
        directories.AddRange(SplitPath(sources.ProcessPath));
        directories.AddRange(SplitPath(sources.MachinePath));
        directories.AddRange(SplitPath(sources.UserPath));
        directories.AddRange(BinDirectories(Clean(sources.CudaPath)));
        foreach (var toolkit in ToolkitDirectories(sources))
            directories.AddRange(BinDirectories(toolkit));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return directories
            .Where(directory => Path.IsPathFullyQualified(directory) && seen.Add(directory.TrimEnd('\\', '/')))
            .ToList();
    }

    /// <summary>The search sources of this PC. Never throws: a source that cannot be read is null.</summary>
    internal static CudaSearchSources FromSystem()
    {
        var processCudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        return new CudaSearchSources(
            Environment.GetEnvironmentVariable("PATH"),
            ReadRegistryValue(Registry.LocalMachine, MachineEnvironmentKey, "Path"),
            ReadRegistryValue(Registry.CurrentUser, UserEnvironmentKey, "Path"),
            string.IsNullOrWhiteSpace(processCudaPath)
                ? ReadRegistryValue(Registry.LocalMachine, MachineEnvironmentKey, "CUDA_PATH")
                : processCudaPath,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            // Most PCs have no toolkit, and a missing folder is not worth an exception on every start.
            folder => Directory.Exists(folder) ? Directory.EnumerateDirectories(folder, ToolkitMajorFolder + "*") : []);
    }

    /// <summary>The <c>v13.*</c> toolkit folders under Program Files, newest first; none when that cannot be listed.</summary>
    private static List<string> ToolkitDirectories(CudaSearchSources sources)
    {
        if (string.IsNullOrWhiteSpace(sources.ProgramFiles))
            return [];

        try
        {
            var toolkits = Path.Combine(sources.ProgramFiles, "NVIDIA GPU Computing Toolkit", "CUDA");
            // Materialised here: a lazy listing fails on its first step, not on the call that returns it.
            return sources.ListDirectories(toolkits)
                .Select(directory => (Folder: directory, Minor: MinorVersion(directory)))
                .Where(toolkit => toolkit.Minor is not null)
                .OrderByDescending(toolkit => toolkit.Minor)
                .Select(toolkit => toolkit.Folder)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Includes DirectoryNotFoundException. The other sources still stand.
            Logger.Warning(ex, "Could not list the CUDA toolkit folders under {ProgramFiles}", sources.ProgramFiles);
            return [];
        }
    }

    /// <summary>The number after <c>v13.</c> in a toolkit folder's name (13.10 is newer than 13.9), or null for another folder.</summary>
    private static int? MinorVersion(string directory)
    {
        var name = Path.GetFileName(directory.TrimEnd('\\', '/'));
        var match = ToolkitFolderName().Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, out var minor) ? minor : null;
    }

    [GeneratedRegex(@"^v13\.(\d{1,9})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolkitFolderName();

    /// <summary>Where a toolkit keeps its DLLs: <c>bin\x64</c> from CUDA 13, <c>bin</c> before it.</summary>
    private static IEnumerable<string> BinDirectories(string? root)
    {
        if (root is null || !Path.IsPathFullyQualified(root))
            yield break;

        yield return Path.Combine(root, "bin", "x64");
        yield return Path.Combine(root, "bin");
    }

    /// <summary>The entries of a PATH-style value: split on <c>;</c>, trimmed and unquoted. May include relative ones.</summary>
    private static IEnumerable<string> SplitPath(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => entry.Trim('"'))
                .Where(entry => entry.Length > 0);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');

    /// <summary>
    /// A string value of a registry key, with %VARIABLES% expanded; null when the key or value is missing, is not a
    /// string, or cannot be read.
    /// </summary>
    private static string? ReadRegistryValue(RegistryKey hive, string subKey, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            // Read unexpanded and expanded here, so the expansion is the same as for the process's own variables.
            return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string value
                ? Environment.ExpandEnvironmentVariables(value)
                : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Logger.Warning(ex, "Could not read {Name} from the registry key {SubKey}", name, subKey);
            return null;
        }
    }
}
