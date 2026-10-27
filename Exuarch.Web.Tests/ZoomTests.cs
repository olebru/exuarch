using Exuarch.Web.Components;
using Exuarch.Web.Hardware;
using Exuarch.Web.Run;

namespace Exuarch.Web.Tests;

public class ZoomTests
{
    [Fact]
    public void EachWheelStepZoomsByTheStepFactor()
    {
        Assert.Equal(1.1, ZoomLimits.ByWheelSteps(1, 1));
        Assert.Equal(1.21, ZoomLimits.ByWheelSteps(1, 2));
        Assert.Equal(0.91, ZoomLimits.ByWheelSteps(1, -1));
    }

    [Fact]
    public void WheelZoomStaysWithinTheLimits()
    {
        Assert.Equal(ZoomLimits.Max, ZoomLimits.ByWheelSteps(1, 50));
        Assert.Equal(ZoomLimits.Min, ZoomLimits.ByWheelSteps(1, -50));
    }

    [Fact]
    public void TheDesignCanvasReturnsTheZoomBeforeAndAfterAWheelStep()
    {
        var interaction = new CanvasInteraction();
        Assert.Equal(new ZoomChange(1, 1.1), interaction.ZoomByWheelSteps(1));
        Assert.Equal(1.1, interaction.Zoom);
    }

    [Fact]
    public void AWheelStepInTheRunViewStopsFittingTheSchematicToThePanel()
    {
        var zoom = new SchematicZoom();
        zoom.Fit(600, 1000);
        Assert.Equal(new ZoomChange(0.57, 0.63), zoom.ByWheelSteps(1));
        Assert.False(zoom.Resized(900, 1000));
        Assert.Equal(0.63, zoom.Zoom);
    }
}
