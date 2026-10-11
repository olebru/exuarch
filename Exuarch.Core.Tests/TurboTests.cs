using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class TurboTests
{
    private static (List<ushort[]> Frames, List<long> Ticks) Swaps(string package, int count)
    {
        var p = BuiltInPackages.Get(package);
        var c = new Machine(p.Machine, p.Programs.Single(x => x.Name == "A spinning cube").Source) { RecordHistory = false };
        var fb = c.Device<DoubleFramebuffer>("fb");
        var frames = new List<ushort[]>();
        var ticks = new List<long>();
        long tick = 0;
        while (frames.Count < count)
        {
            c.SingleStep();
            tick++;
            if (fb.Swaps == frames.Count) continue;
            frames.Add((ushort[])fb.Shown.Clone());
            ticks.Add(tick);
        }
        return (frames, ticks);
    }

    [Fact]
    public void TheCubeLooksTheSameAsFlip16AndIsDrawnInUnderAFifthOfTheTicks()
    {
        var flip = Swaps("FLIP-16", 10);
        var turbo = Swaps("TURBO-16", 130);
        for (int n = 1; n < 10; n++) Assert.True(flip.Frames[n].SequenceEqual(turbo.Frames[n]), $"angle {n - 1} differs");
        Assert.Contains(turbo.Frames[2], pixel => pixel != 0);

        Assert.Equal(148378, flip.Ticks[1]);
        Assert.Equal(32821, turbo.Ticks[1]);
        double replay = (turbo.Ticks[129] - turbo.Ticks[66]) / 63.0;
        Assert.Equal(24903, (int)System.Math.Round(replay));
        double flipFrames = (flip.Ticks[9] - flip.Ticks[2]) / 7.0, turboFrames = (turbo.Ticks[9] - turbo.Ticks[2]) / 7.0;
        Assert.True(turboFrames * 5 < flipFrames, $"{turboFrames} against {flipFrames}");

        var readme = BuiltInPackages.Get("TURBO-16").Readme;
        foreach (var text in new[] { "24,903", "32,821", "148,378" }) Assert.Contains(text, readme);
    }
}
