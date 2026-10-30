using Shouldly;

namespace Murmur.UI.Tests;

public sealed class LaunchArgumentsTests
{
    [Fact]
    public void Parse_NoArguments_NothingIsRequested()
    {
        LaunchArguments.Parse([]).ShouldBe(new LaunchArguments(Minimized: false, Restarted: false, Page: null));
    }

    [Fact]
    public void Parse_RestartedWithPage_ReadsBoth()
    {
        LaunchArguments.Parse(["--restarted", "--page", "settings"])
            .ShouldBe(new LaunchArguments(Minimized: false, Restarted: true, Page: "settings"));
    }

    /// <summary>What Restart Murmur starts the new instance with must be what that instance understands.</summary>
    [Fact]
    public void Parse_TheArgumentsOfARestart_AreARestartOnSettings()
    {
        LaunchArguments.Parse(LaunchArguments.RestartArguments)
            .ShouldBe(new LaunchArguments(Minimized: false, Restarted: true, Page: "settings"));
    }

    [Fact]
    public void Parse_UnknownPage_IsIgnored()
    {
        LaunchArguments.Parse(["--page", "evil"]).Page.ShouldBeNull();
    }

    [Fact]
    public void Parse_Minimized_IsCaseInsensitive()
    {
        LaunchArguments.Parse(["--MINIMIZED"]).Minimized.ShouldBeTrue();
        LaunchArguments.Parse(["--Minimized"]).Minimized.ShouldBeTrue();
    }

    [Theory]
    [InlineData("history", "history")]
    [InlineData("Vocabulary", "vocabulary")]
    [InlineData("SETTINGS", "settings")]
    public void Parse_Page_IsCaseInsensitiveAndComesBackAsTheWebAppSpellsIt(string page, string expected)
    {
        LaunchArguments.Parse(["--PAGE", page]).Page.ShouldBe(expected);
    }

    [Fact]
    public void Parse_PageWithoutAValue_IsIgnored()
    {
        LaunchArguments.Parse(["--restarted", "--page"])
            .ShouldBe(new LaunchArguments(Minimized: false, Restarted: true, Page: null));
    }

    /// <summary>A missing page value must not swallow the flag after it.</summary>
    [Fact]
    public void Parse_PageFollowedByAnotherFlag_StillReadsThatFlag()
    {
        LaunchArguments.Parse(["--page", "--minimized"])
            .ShouldBe(new LaunchArguments(Minimized: true, Restarted: false, Page: null));
    }

    [Fact]
    public void Parse_UnknownArguments_AreIgnored()
    {
        LaunchArguments.Parse(["--surprise", "C:\\somewhere\\file.txt", "--minimized"])
            .ShouldBe(new LaunchArguments(Minimized: true, Restarted: false, Page: null));
    }
}
