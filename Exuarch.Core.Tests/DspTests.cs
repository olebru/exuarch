using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class DspTests
{
    private static readonly MachinePackage Dsp = BuiltInPackages.Get("DSP-16");

    private static Machine Start(string source) => new Machine(Dsp.Machine, source) { RecordHistory = false };
    private static int R(Machine c, int n) => c.Device<RegisterFile>("rf").Values[n];

    private const int FixedPointOne = 1 << 12;

    [Theory]
    [InlineData(2 * FixedPointOne, 3 * FixedPointOne / 2, 3 * FixedPointOne)]
    [InlineData(-2 * FixedPointOne, 3 * FixedPointOne / 2, -3 * FixedPointOne)]
    [InlineData(FixedPointOne / 4, FixedPointOne / 4, FixedPointOne / 16)]
    [InlineData(1, 1, 0)]
    [InlineData(32767, 32767, 32767)]
    public void MulMultipliesIn4Dot12FixedPointAndClamps(int a, int b, int product)
    {
        var c = Start($"MOVI R1, {a}\nMOVI R2, {b}\nMUL R0, R1, R2\nHLT");
        while (!c.IsHalted) c.SingleStep();
        Assert.Equal(product, (short)R(c, 0));
        Assert.Equal(a & 0xFFFF, R(c, 1));
    }

    private static int Mul(int a, int b) => (int)Math.Clamp(((long)(short)a * (short)b) >> 12, short.MinValue, short.MaxValue) & 0xFFFF;
    private static int Colour(int cx, int cy, int steps, Func<int, int> palette)
    {
        int x = cx, y = cy;
        for (int left = steps; left > 0; left--)
        {
            int x2 = Mul(x, x), y2 = Mul(y, y);
            if (((x2 + y2) & 0xFFFF) >= 16385) return palette((left - 1) & 31);
            int xy = Mul(x, y);
            y = (xy + xy + cy) & 0xFFFF;
            x = (x2 - y2 + cx) & 0xFFFF;
        }
        return 0;
    }

    private record Picture(string Name, int Width, int Block, int Left, int Top, int Step, int Steps);
    private static readonly Picture[] Pictures =
    {
        new("Mandelbrot, 80 x 60", 80, 8, -10240, -5760, 192, 32),
        new("Mandelbrot, 160 x 120", 160, 4, -10240, -5760, 96, 32),
        new("Mandelbrot, 640 x 480", 640, 1, -10240, -5760, 24, 32),
        new("Lightning at the top of the set", 640, 1, -734, 3677, 1, 128),
    };

    private static void AssertDrawn(Machine c, Picture picture, int rows)
    {
        var mem = c.Device<RamModule>("mem");
        int palette = c.Assembler.labelLUT["palette"];
        var fb = c.Device<Framebuffer>("fb");
        for (int row = 0; row < rows; row++)
        {
            int cy = (picture.Top + row * picture.Step) & 0xFFFF;
            for (int column = 0; column < picture.Width; column++)
            {
                int cx = (picture.Left + column * picture.Step) & 0xFFFF;
                int expected = Colour(cx, cy, picture.Steps, k => mem.ValueAt(palette + k));
                for (int dy = 0; dy < picture.Block; dy++)
                {
                    for (int dx = 0; dx < picture.Block; dx++)
                    {
                        int pixel = fb.Pixels[(row * picture.Block + dy) * Framebuffer.Width + column * picture.Block + dx];
                        Assert.True(expected == pixel, $"{picture.Name}: point {column}, {row} is {pixel:X4}, not {expected:X4}");
                    }
                }
            }
        }
    }

    [Fact]
    public void TheCoarsePictureIsTheMandelbrotSet()
    {
        var picture = Pictures[0];
        var c = Start(Dsp.Program(picture.Name).Source);
        long ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); ticks++; }
        Assert.Empty(c.MicrocodeWarnings);
        AssertDrawn(c, picture, 60);
        Assert.Equal(CoarseTicks, ticks);
        var fb = c.Device<Framebuffer>("fb");
        Assert.Equal(0, fb.Pixels[240 * Framebuffer.Width + 100]);
        Assert.Equal(0, fb.Pixels[240 * Framebuffer.Width + 400]);
        Assert.NotEqual(0, fb.Pixels[0]);
    }
    private const long CoarseTicks = 8_239_344;

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    public void TheFinerPicturesDrawTheirFirstRows(int index, int rows)
    {
        var picture = Pictures[index];
        var c = Start(Dsp.Program(picture.Name).Source);
        var fb = c.Device<Framebuffer>("fb");
        while (fb.Y < rows * picture.Block) c.SingleStep();
        Assert.Empty(c.MicrocodeWarnings);
        AssertDrawn(c, picture, rows);
    }

    [Fact]
    public void TheReadmeListsTheTicksOfEveryPicture()
    {
        foreach (var ticks in AllTicks) Assert.Contains($"{ticks:N0} ticks", Dsp.Readme);
    }
    private static readonly long[] AllTicks = { CoarseTicks, 25_605_802, 353_232_756, 583_757_144 };

    [Fact]
    public void ThePicturesDifferOnlyInTheirNumbers()
    {
        string Shape(string source) => System.Text.RegularExpressions.Regex.Replace(
            string.Join("\n", source.Split('\n').Select(l => l.Split(';')[0].TrimEnd()).Where(l => l.Length > 0 && !l.Contains("PLOT"))), @"-?\b(0x)?[0-9A-F]+\b", "#");
        Assert.Equal(4, Dsp.Programs.Count);
        Assert.Single(Dsp.Programs.Select(p => Shape(p.Source)).Distinct());
    }
}
