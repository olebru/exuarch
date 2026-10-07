using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class SchematicGeometryTests
{
    [Fact]
    public void DistanceIsAlongTheGridLines()
    {
        Assert.Equal(7, new Point(1, 2).Distance(new Point(4, 6)));
        Assert.Equal(new Point(3, 0), new Point(0, 0).Toward(new Point(10, 0), 3));
        Assert.Equal(new Point(5, 5), new Point(5, 5).Toward(new Point(5, 5), 3));
    }

    [Fact]
    public void ASegmentKnowsItsLineAndExtent()
    {
        var vertical = new Segment(new Point(4, 9), new Point(4, 2));
        Assert.False(vertical.Horizontal);
        Assert.Equal((4.0, 2.0, 9.0), (vertical.At, vertical.From, vertical.To));
        var horizontal = new Segment(new Point(8, 3), new Point(1, 3));
        Assert.True(horizontal.Horizontal);
        Assert.Equal((3.0, 1.0, 8.0, 7.0), (horizontal.At, horizontal.From, horizontal.To, horizontal.Length));
        // Either way round it is the same stretch.
        Assert.Equal(horizontal.Normalized, new Segment(horizontal.B, horizontal.A).Normalized);
    }

    [Fact]
    public void ARectangleCountsOnlyItsInside()
    {
        var r = new Rect(10, 10, 20, 10);
        Assert.Equal((30.0, 20.0), (r.Right, r.Bottom));
        Assert.Equal(new Point(20, 15), r.Centre);
        Assert.True(r.Contains(new Point(15, 15)));
        Assert.False(r.Contains(new Point(10, 15)));
        Assert.Equal(new Rect(8, 8, 24, 14), r.Inflate(2));
        // Through the inside crosses it, along an edge does not.
        Assert.True(r.Crosses(new Segment(new Point(0, 15), new Point(40, 15))));
        Assert.False(r.Crosses(new Segment(new Point(0, 10), new Point(40, 10))));
        Assert.False(r.Crosses(new Segment(new Point(15, 0), new Point(15, 5))));
    }

    [Fact]
    public void RectanglesIntersectWhenCloserThanTheGap()
    {
        var a = new Rect(0, 0, 10, 10);
        Assert.False(a.Intersects(new Rect(10, 0, 10, 10)));
        Assert.True(a.Intersects(new Rect(9, 0, 10, 10)));
        Assert.True(a.Intersects(new Rect(15, 0, 10, 10), 6));
        Assert.False(a.Intersects(new Rect(15, 0, 10, 10), 5));
    }

    [Fact]
    public void ObstaclesAreTheCardsAndTheLinesNotToRunAlong()
    {
        var obstacles = new Obstacles(new[] { new Rect(0, 0, 10, 10) }, new[] { 50.0 }, new[] { new Segment(new Point(100, 0), new Point(100, 50)) });
        Assert.True(obstacles.Contains(new Point(5, 5)));
        Assert.False(obstacles.Contains(new Point(15, 5)));
        Assert.True(obstacles.Crosses(new Segment(new Point(-5, 5), new Point(15, 5))));
        Assert.True(obstacles.Inflate(10).Contains(new Point(15, 5)));
        // Along the bus and the tap, but crossing them is fine.
        Assert.True(obstacles.RunsAlong(new Segment(new Point(20, 53), new Point(80, 53)), 7));
        Assert.False(obstacles.RunsAlong(new Segment(new Point(20, 60), new Point(80, 60)), 7));
        Assert.True(obstacles.RunsAlong(new Segment(new Point(104, 10), new Point(104, 20)), 7));
        Assert.False(obstacles.RunsAlong(new Segment(new Point(104, 60), new Point(104, 70)), 7));
        Assert.False(obstacles.RunsAlong(new Segment(new Point(90, 20), new Point(110, 20)), 7));
        // A clear area keeps the gap to the cards and the buses.
        Assert.True(obstacles.IsClear(new Rect(40, 0, 10, 10), 20));
        Assert.False(obstacles.IsClear(new Rect(25, 0, 10, 10), 20));
        Assert.False(obstacles.IsClear(new Rect(40, 60, 10, 10), 20));
    }
}
