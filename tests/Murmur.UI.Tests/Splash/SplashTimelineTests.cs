using Murmur.UI.Splash;
using Shouldly;

namespace Murmur.UI.Tests.Splash;

/// <summary>The beats of the splash, as the design's timeline gives them (spec §5.1).</summary>
public sealed class SplashTimelineTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Morph_RunsFromNineTenthsOfASecondToOnePointSixFive()
    {
        SplashTimeline.Morph(0).ShouldBe(0);
        SplashTimeline.Morph(0.9).ShouldBe(0, Tolerance);
        SplashTimeline.Morph(1.275).ShouldBe(0.5, 0.01);
        SplashTimeline.Morph(1.65).ShouldBe(1, Tolerance);
        SplashTimeline.Morph(9).ShouldBe(1);
    }

    [Fact]
    public void WaveAmplitude_SwellsInAQuarterSecondAndIsGoneWhenTheLineHasSettled()
    {
        SplashTimeline.WaveAmplitude(0).ShouldBe(0);
        SplashTimeline.WaveAmplitude(0.25).ShouldBe(1, Tolerance);
        SplashTimeline.WaveAmplitude(0.9).ShouldBe(1, Tolerance);
        SplashTimeline.WaveAmplitude(1.65).ShouldBe(0, Tolerance);
    }

    [Fact]
    public void TileReveal_RunsFromOnePointFourFiveToOnePointNine()
    {
        SplashTimeline.TileReveal(1.45).ShouldBe(0, Tolerance);
        SplashTimeline.TileReveal(1.9).ShouldBe(1, Tolerance);
    }

    [Fact]
    public void StatusOpacity_FadesInFromThreeToSixTenthsOfASecond()
    {
        SplashTimeline.StatusOpacity(0.3).ShouldBe(0, Tolerance);
        SplashTimeline.StatusOpacity(0.45).ShouldBe(0.5, Tolerance);
        SplashTimeline.StatusOpacity(0.6).ShouldBe(1, Tolerance);
        SplashTimeline.StatusOpacity(9).ShouldBe(1);
    }

    [Theory]
    [InlineData(1.74, 0)]
    [InlineData(1.75, 1)]
    [InlineData(1.84, 2)]
    [InlineData(2.17, 5)]
    [InlineData(2.18, 6)]
    [InlineData(9, 6)]
    public void TypedLetters_OneEveryEightyFiveMilliseconds(double time, int expected)
    {
        SplashTimeline.TypedLetters(time).ShouldBe(expected);
    }

    [Fact]
    public void TypedLetters_NeverMoreThanTheWordHas()
    {
        SplashTimeline.Word.ShouldBe("Murmur");
        SplashTimeline.TypedLetters(SplashTimeline.End).ShouldBe(SplashTimeline.Word.Length);
    }

    /// <summary>
    /// The splash closes 2.5 s in at the earliest. The name must have stood finished for a moment by then: measured
    /// with typing ending at 2.375 s, the whole word was on screen for about a tenth of a second.
    /// </summary>
    [Fact]
    public void TypedLetters_TheNameIsFinishedAThirdOfASecondBeforeTheSplashCanClose()
    {
        SplashTimeline.TypedLetters(2.5 - 0.3).ShouldBe(SplashTimeline.Word.Length);
    }

    [Fact]
    public void CaretVisible_ShowsWhileTypingBlinksAfterwardsAndGoes()
    {
        SplashTimeline.CaretVisible(1.5).ShouldBeFalse();
        SplashTimeline.CaretVisible(1.6).ShouldBeTrue();
        SplashTimeline.CaretVisible(2.0).ShouldBeTrue();

        // Typing ends at 2.175 s; from there it blinks every 0.4 s for about a second.
        SplashTimeline.CaretVisible(2.2).ShouldBeTrue();
        SplashTimeline.CaretVisible(2.7).ShouldBeFalse();
        SplashTimeline.CaretVisible(3.1).ShouldBeTrue();
        SplashTimeline.CaretVisible(3.5).ShouldBeFalse();
        SplashTimeline.CaretVisible(SplashTimeline.End).ShouldBeFalse();
    }

    [Fact]
    public void End_IsWhereEverythingHasCometoRest()
    {
        SplashTimeline.End.ShouldBe(4.3);
        SplashTimeline.Morph(SplashTimeline.End).ShouldBe(1);
        SplashTimeline.TileReveal(SplashTimeline.End).ShouldBe(1);
    }
}
