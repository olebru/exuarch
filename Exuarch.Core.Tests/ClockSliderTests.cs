using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ClockSliderTests
{
    [Fact]
    public void TheSliderRunsFromOneHertzToOneMegahertz()
    {
        Assert.Equal(1, ClockSlider.HzAt(0));
        Assert.Equal(1_000_000, ClockSlider.HzAt(ClockSlider.Steps));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(1000)]
    [InlineData(250_000)]
    [InlineData(1_000_000)]
    public void CommonSpeedsHaveAStep(int hz)
    {
        Assert.Equal(hz, ClockSlider.HzAt(ClockSlider.StepFor(hz).Value));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(980_000)]
    [InlineData(12_345)]
    public void SomeSpeedsHaveNoStep(int hz)
    {
        Assert.Null(ClockSlider.StepFor(hz));
        Assert.NotNull(ClockSlider.StepFor(ClockSlider.Nearest(hz)));
    }

    [Fact]
    public void NearestPicksTheCloserStep()
    {
        Assert.Equal(12_000, ClockSlider.Nearest(12_345));
        Assert.Equal(250_000, ClockSlider.Nearest(250_000));
        Assert.Equal(1_000_000, ClockSlider.Nearest(5_000_000));
    }

    [Fact]
    public void StepAtLeastIsNullAboveTheTop()
    {
        Assert.Null(ClockSlider.StepAtLeast(2_000_000));
        Assert.True(ClockSlider.HzAt(ClockSlider.StepAtLeast(99).Value) >= 99);
    }

    [Fact]
    public void AnExactSpeedRoundTripsInPackageJson()
    {
        var package = BuiltInPackages.Get("IRQ-16");
        package.Programs[0].Needs = new ProgramNeeds { ExactHz = 1000 };
        Assert.Equal(1000, MachinePackage.FromJson(package.ToJson()).Programs[0].Needs.ExactHz);
        Assert.False(package.Programs[0].Needs.IsEmpty());
        Assert.True(new ProgramNeeds().IsEmpty());
    }
}
