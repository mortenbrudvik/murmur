using Murmur.Core.Services;
using Shouldly;
using Windows.ApplicationModel;

namespace Murmur.Core.Tests.Services;

/// <summary>
/// The packaged path reduces to this decision table plus WinRT calls that need a real package, which a test host never
/// has; those are covered by running the staged package (docs/store-submission-runbook.md).
/// </summary>
public sealed class PackagedStartupRegistrationTests
{
    /// <summary>
    /// DisabledByUser is the user's veto from Task Manager: asking Windows to enable the task would change nothing (it
    /// does not override the user), so it is reported, never retried as if it might work.
    /// </summary>
    [Theory]
    [InlineData(true, StartupTaskState.Disabled, StartupTaskChange.Enable)]
    [InlineData(true, StartupTaskState.Enabled, StartupTaskChange.None)]
    [InlineData(true, StartupTaskState.EnabledByPolicy, StartupTaskChange.None)]
    [InlineData(true, StartupTaskState.DisabledByUser, StartupTaskChange.RefusedByUser)]
    [InlineData(true, StartupTaskState.DisabledByPolicy, StartupTaskChange.RefusedByPolicy)]
    [InlineData(false, StartupTaskState.Enabled, StartupTaskChange.Disable)]
    [InlineData(false, StartupTaskState.Disabled, StartupTaskChange.None)]
    [InlineData(false, StartupTaskState.DisabledByUser, StartupTaskChange.None)]
    [InlineData(false, StartupTaskState.DisabledByPolicy, StartupTaskChange.None)]
    [InlineData(false, StartupTaskState.EnabledByPolicy, StartupTaskChange.RefusedByPolicy)]
    public void Plan_CoversEveryStartupTaskState(bool enabled, StartupTaskState state, StartupTaskChange expected)
    {
        PackagedStartupRegistration.Plan(enabled, state).ShouldBe(expected);
    }

    [Theory]
    [InlineData(StartupTaskState.Enabled, true)]
    [InlineData(StartupTaskState.EnabledByPolicy, true)]
    [InlineData(StartupTaskState.Disabled, false)]
    [InlineData(StartupTaskState.DisabledByUser, false)]
    [InlineData(StartupTaskState.DisabledByPolicy, false)]
    public void IsEnabled_IsTrueOnlyWhenMurmurWillStartAtSignIn(StartupTaskState state, bool expected)
    {
        PackagedStartupRegistration.IsEnabled(state).ShouldBe(expected);
    }

    /// <summary>The TaskId must match the manifest's uap5:StartupTask TaskId exactly, or GetAsync answers null.</summary>
    [Fact]
    public void TaskId_MatchesTheManifest()
    {
        var manifest = File.ReadAllText(FindRepoFile("packaging", "msix", "AppxManifest.xml"));

        manifest.ShouldContain($"TaskId=\"{PackagedStartupRegistration.TaskId}\"");
    }

    /// <summary>
    /// Outside a package there is no startup task to fetch. The work behind Apply must log that and finish, never fault:
    /// Apply fires it and forgets it, on the UI thread.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WithoutPackageIdentity_CompletesWithoutFaulting()
    {
        var registration = new PackagedStartupRegistration();

        await registration.ApplyAsync(true);

        registration.IsRegistered.ShouldBeFalse();
    }

    private static string FindRepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"{Path.Combine(parts)} not found above {AppContext.BaseDirectory}");
    }
}
