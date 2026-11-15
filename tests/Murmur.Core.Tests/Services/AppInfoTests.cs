using System.Reflection;
using System.Text.Json;
using Murmur.Core.Services;
using NSubstitute;
using Shouldly;

namespace Murmur.Core.Tests.Services;

public sealed class AppInfoTests
{
    [Theory]
    [InlineData("1.1.0", "1.1.0")]
    [InlineData("1.1.0+3f2a9c1", "1.1.0")]
    [InlineData("1.1.0.0", "1.1.0")]
    [InlineData(null, "0.0.0")]
    public void FormatVersion_KeepsThreeParts(string? informational, string expected)
    {
        AppInfo.FormatVersion(informational).ShouldBe(expected);
    }

    [Theory]
    [InlineData(26200, "Windows 11, build 26200")]
    [InlineData(22000, "Windows 11, build 22000")]
    [InlineData(19045, "Windows 10, build 19045")]
    public void FormatWindows_NamesTheReleaseByBuild(int build, string expected)
    {
        AppInfo.FormatWindows(new Version(10, 0, build)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, "Microsoft Store")]
    [InlineData(false, "Built from source")]
    public void Channel_FollowsPackageIdentity(bool packaged, string expected)
    {
        new AppInfo(Identity(packaged), new MurmurPaths(@"C:\x")).Channel.ShouldBe(expected);
    }

    /// <summary>
    /// Pins the wiring in Directory.Build.props: every assembly carries the version in version.json. Symptom if this
    /// regresses: About and the splash show 1.0.0 (the SDK's default) whatever the package says.
    /// </summary>
    [Fact]
    public void Version_IsTheOneInVersionJson()
    {
        new AppInfo(Identity(false), new MurmurPaths(@"C:\x")).Version.ShouldBe(VersionInVersionJson());
    }

    [Fact]
    public void DataFolder_IsThePathsRoot()
    {
        new AppInfo(Identity(false), new MurmurPaths(@"C:\data\Murmur")).DataFolder.ShouldBe(@"C:\data\Murmur");
    }

    [Fact]
    public void WindowsBuild_DescribesThisPc()
    {
        new AppInfo(Identity(false), new MurmurPaths(@"C:\x")).WindowsBuild
            .ShouldBe(AppInfo.FormatWindows(Environment.OSVersion.Version));
    }

    [Theory]
    [InlineData("2026", 2026)]
    [InlineData("2031", 2031)]
    public void ParseBuildYear_ReadsTheYearTheBuildStamped(string stamped, int expected)
    {
        AppInfo.ParseBuildYear(stamped, fallback: 1999).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("26")]
    public void ParseBuildYear_NothingUsableStamped_FallsBack(string? stamped)
    {
        AppInfo.ParseBuildYear(stamped, fallback: 1999).ShouldBe(1999);
    }

    /// <summary>
    /// Pins the wiring in Directory.Build.props: every assembly carries the year it was built in, for the About
    /// window's copyright line. Symptom if this regresses: the line shows the year the app is run in.
    /// </summary>
    [Fact]
    public void BuildYear_IsStampedIntoTheAssembly()
    {
        typeof(AppInfo).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == "BuildYear")
            .Select(attribute => attribute.Value)
            .ShouldBe([new AppInfo(Identity(false), new MurmurPaths(@"C:\x")).BuildYear.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    private static IPackageIdentity Identity(bool packaged)
    {
        var identity = Substitute.For<IPackageIdentity>();
        identity.IsPackaged.Returns(packaged);
        return identity;
    }

    private static string VersionInVersionJson()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "version.json");
            if (File.Exists(file))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                return document.RootElement.GetProperty("version").GetString()!;
            }
        }

        throw new FileNotFoundException("version.json was not found above the test output folder.");
    }
}
