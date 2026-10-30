using System.Collections.Specialized;
using Murmur.UI.Pill;
using Shouldly;

namespace Murmur.UI.Tests.Pill;

public class PillViewModelTests
{
    [Theory]
    [InlineData(-60f, 0.2)]
    [InlineData(-20f, 1.0)]
    [InlineData(-40f, 0.6)]
    [InlineData(-100f, 0.2)]
    [InlineData(0f, 1.0)]
    public void PushLevel_MapsDbfsLinearlyAndClamps(float dbfs, double expected)
    {
        var vm = new PillViewModel();

        vm.PushLevel(dbfs);

        vm.Bars[^1].ShouldBe(expected, 0.001);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void PushLevel_NotANumberOrMinusInfinity_IsTheMinimumBar(float dbfs)
    {
        var vm = new PillViewModel();

        vm.PushLevel(dbfs);

        vm.Bars[^1].ShouldBe(0.2, 0.001);
    }

    [Fact]
    public void PushLevel_PositiveInfinity_IsTheMaximumBar()
    {
        var vm = new PillViewModel();

        vm.PushLevel(float.PositiveInfinity);

        vm.Bars[^1].ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void Bars_Initially_AreEightMinimumBars()
    {
        var vm = new PillViewModel();

        vm.Bars.Count.ShouldBe(8);
        vm.Bars.ShouldAllBe(bar => bar == 0.2);
    }

    [Fact]
    public void PushLevel_TenPushes_ShiftsLeftAndKeepsEight()
    {
        var vm = new PillViewModel();

        for (var i = 0; i < 10; i++)
        {
            vm.PushLevel(-60f + 4f * i); // 0.2, 0.28, ... 0.92
        }

        vm.Bars.Count.ShouldBe(8);
        vm.Bars[^1].ShouldBe(0.2 + 0.8 * 36 / 40, 0.001); // the latest push (-24 dBFS)
        vm.Bars[0].ShouldBe(0.2 + 0.8 * 8 / 40, 0.001);   // the 3rd push (-52 dBFS); the first two fell off
    }

    [Fact]
    public void PushLevel_ShiftsInPlaceWithoutRemovingItems()
    {
        var vm = new PillViewModel();
        var actions = new List<NotifyCollectionChangedAction>();
        vm.Bars.CollectionChanged += (_, e) => actions.Add(e.Action);

        for (var i = 0; i < 10; i++)
        {
            vm.PushLevel(-60f + 4f * i);
        }

        actions.ShouldNotBeEmpty();
        actions.ShouldAllBe(action => action == NotifyCollectionChangedAction.Replace);
        vm.Bars.Count.ShouldBe(8);
    }

    [Fact]
    public void PushLevel_FallsAtMost0_15PerStep()
    {
        var vm = new PillViewModel();
        vm.PushLevel(-20f); // 1.0

        var expected = new[] { 0.85, 0.70, 0.55, 0.40, 0.25, 0.20 }; // silence pulls the bar down 0.15 at a time
        foreach (var bar in expected)
        {
            vm.PushLevel(-60f);

            vm.Bars[^1].ShouldBe(bar, 0.001);
        }
    }

    [Fact]
    public void PushLevel_QuieterByLessThanTheLimit_FollowsTheLevel()
    {
        var vm = new PillViewModel();
        vm.PushLevel(-20f); // 1.0

        vm.PushLevel(-24f); // 0.92, a fall of 0.08

        vm.Bars[^1].ShouldBe(0.92, 0.001);
    }

    [Fact]
    public void PushLevel_Louder_RisesAtOnce()
    {
        var vm = new PillViewModel();
        vm.PushLevel(-60f); // 0.2

        vm.PushLevel(-20f);

        vm.Bars[^1].ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void PushLevel_KeepsBarsWithinRange()
    {
        var vm = new PillViewModel();

        foreach (var dbfs in new[] { -200f, -60f, -45f, -20f, 6f, float.NaN })
        {
            vm.PushLevel(dbfs);
        }

        vm.Bars.ShouldAllBe(bar => bar >= 0.2 && bar <= 1.0);
    }

    [Fact]
    public void Mode_Initially_IsHidden()
    {
        new PillViewModel().Mode.ShouldBe(PillMode.Hidden);
    }

    [Fact]
    public void Mode_EnteringListening_ResetsTheBars()
    {
        var vm = new PillViewModel();
        vm.PushLevel(-20f);
        vm.PushLevel(-20f);

        vm.Mode = PillMode.Listening;

        vm.Bars.Count.ShouldBe(8);
        vm.Bars.ShouldAllBe(bar => bar == 0.2);
    }

    [Fact]
    public void Mode_SwitchingAwayFromListening_KeepsTheBars()
    {
        var vm = new PillViewModel { Mode = PillMode.Listening };
        vm.PushLevel(-20f);

        vm.Mode = PillMode.Transcribing;

        vm.Bars[^1].ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void Mode_Changes_RaisePropertyChanged()
    {
        var vm = new PillViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Mode = PillMode.Notice;
        vm.NoticeText = "Saved to history";

        raised.ShouldBe([nameof(PillViewModel.Mode), nameof(PillViewModel.NoticeText)]);
    }
}
