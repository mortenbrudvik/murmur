using System.Reflection;

namespace Murmur.Core.Services;

/// <summary>What Murmur can say about itself: the About dialog, the splash and "Copy details" all read it from here.</summary>
public interface IAppInfo
{
    /// <summary>The three-part version from <c>version.json</c> ("1.1.0").</summary>
    string Version { get; }

    /// <summary>How this copy was installed: "Microsoft Store" or "Built from source".</summary>
    string Channel { get; }

    /// <summary>The folder that holds settings, history, the model and logs.</summary>
    string DataFolder { get; }

    /// <summary>"Windows 11, build 26200".</summary>
    string WindowsBuild { get; }
}

public sealed class AppInfo(IPackageIdentity identity, MurmurPaths paths) : IAppInfo
{
    /// <summary>The first build of Windows 11; both releases report version 10.0.</summary>
    private const int FirstWindows11Build = 22000;

    /// <summary>
    /// Read from this assembly: Directory.Build.props gives every project the version in <c>version.json</c>.
    /// </summary>
    public string Version { get; } = FormatVersion(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public string Channel { get; } = identity.IsPackaged ? "Microsoft Store" : "Built from source";

    public string DataFolder { get; } = paths.Root;

    public string WindowsBuild { get; } = FormatWindows(Environment.OSVersion.Version);

    /// <summary>Keeps at most three parts and drops a build suffix: "1.1.0+3f2a9c1" and "1.1.0.0" both give "1.1.0".</summary>
    internal static string FormatVersion(string? informationalVersion)
    {
        var plain = informationalVersion?.Split('+', '-')[0];
        return System.Version.TryParse(plain, out var version)
            ? $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}"
            : "0.0.0";
    }

    internal static string FormatWindows(Version os) =>
        $"Windows {(os.Build >= FirstWindows11Build ? 11 : 10)}, build {os.Build}";
}
