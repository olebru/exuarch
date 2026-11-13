using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class TexturedRasterizerTests
{
    private const int TextureAddress = 100;
    private const int White = 0xFFFF;

    private sealed class Rig
    {
        public readonly Bus Host = new Bus("host"), List = new Bus("list"), Texture = new Bus("tex"), Video = new Bus("video");
        public readonly Register Cpu;
        public readonly RamModule Triangles, Texels;
        public readonly Framebuffer Screen;
        public readonly DepthBuffer Depth;
        public readonly TexturedRasterizer Rasterizer;
        public IBusDevice[] Devices;

        public Rig(bool withDepth, int size = 4, int[] order = null)
        {
            Cpu = new Register("CPU", "cpu", Host);
            Triangles = new RamModule("LIST", "lmem", List, 1024);
            Texels = new RamModule("TEX", "tmem", Texture, 1024);
            Screen = new Framebuffer("FB", "fb", Video);
            Depth = withDepth ? new DepthBuffer("Z", "zb", Video) : null;
            Rasterizer = new TexturedRasterizer("TRAST", "trast", Host, List, Texture, Video, Screen, Depth, Triangles, Texels, size);
            var parts = new List<IBusDevice> { Cpu, Rasterizer, Triangles, Texels, Screen };
            if (Depth != null) parts.Add(Depth);
            Devices = order == null ? parts.ToArray() : order.Select(i => parts[i]).ToArray();
            Texels.LoadProgram(Enumerable.Range(0, TextureAddress + size * size).Select(i => i < TextureAddress ? 0 : 0x0841 * ((i - TextureAddress) % 31 + 1) & 0xFFFF).ToArray());
        }

        public int TexelAt(int u, int v, int size = 4) => Texels.ValueAt(TextureAddress + v * size + u);

        public void Tick() => Clocking.Tick(new[] { Host, List, Texture, Video }, Devices);

        public void Load(string line, int value)
        {
            Cpu.Data = value;
            Cpu.Enable("output");
            Rasterizer.Enable(line);
            Tick();
        }

        public int Draw(params int[][] triangles)
        {
            Triangles.LoadProgram(triangles.SelectMany(t => t).ToArray());
            Load("loadaddr", 0);
            Load("loadcount", triangles.Length);
            Rasterizer.Enable("start");
            Tick();
            int ticks = 0;
            while (Rasterizer.Busy)
            {
                Tick();
                Assert.True(++ticks < 200_000, "the rasterizer never finished");
            }
            return ticks;
        }

        public int Pixel(int x, int y) => Screen.Pixels[y * Framebuffer.Width + x];
    }

    private static int[] Triangle(params (int X, int Y, int Z, int U, int V, int Shade)[] corners)
    {
        return corners.SelectMany(c => new[] { c.X, c.Y, c.Z, c.U, c.V, c.Shade }).Append(TextureAddress).ToArray();
    }

    private static int[][] Square(int left, int top, int side, int uv, int zLeft = 0, int zRight = 0, int shade = White)
    {
        return new[]
        {
            Triangle((left, top, zLeft, 0, 0, shade), (left + side, top, zRight, uv, 0, shade), (left + side, top + side, zRight, uv, uv, shade)),
            Triangle((left, top, zLeft, 0, 0, shade), (left + side, top + side, zRight, uv, uv, shade), (left, top + side, zLeft, 0, uv, shade)),
        };
    }

    [Fact]
    public void LinesMatchTheRegistry()
    {
        var rig = new Rig(withDepth: false);
        var info = DeviceRegistry.CreateDefault().Info("texturedRasterizer");
        Assert.Equal(rig.Rasterizer.SignalLines(), info.ControlLines.Select(l => l.Name));
        Assert.Equal(new[] { "host", "list", "tex", "video" }, info.Ports);
        Assert.Equal(new[] { "screen", "depth", "memory", "texture" }, info.Connections.Select(c => c.Name));
        Assert.Equal(19, TexturedRasterizer.WordsPerTriangle);
    }

    [Fact]
    public void AFaceOnSquareShowsEachTexelAsATwoByTwoBlock()
    {
        var rig = new Rig(withDepth: false);
        rig.Draw(Square(10, 10, 8, 4));
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(rig.TexelAt(x / 2, y / 2), rig.Pixel(10 + x, 10 + y));
        Assert.Equal(0, rig.Pixel(18, 10));
        Assert.Equal(0, rig.Pixel(10, 18));
        Assert.Equal(64, rig.Rasterizer.PixelsDrawn);
        Assert.Equal(64, rig.Rasterizer.TexelsRead);
    }

    [Fact]
    public void TexturePositionsWrapRoundTheSize()
    {
        var rig = new Rig(withDepth: false);
        rig.Draw(Square(0, 0, 8, 8));
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(rig.TexelAt(x % 4, y % 4), rig.Pixel(x, y));
    }

    [Fact]
    public void TheShadeMultipliesEachChannel()
    {
        Assert.Equal(0xF81F, TexturedRasterizer.TexturedTriangle.Modulate(0xF81F, White));
        Assert.Equal(0, TexturedRasterizer.TexturedTriangle.Modulate(0xFFFF, 0));
        Assert.Equal(0xF800, TexturedRasterizer.TexturedTriangle.Modulate(0xFFFF, 0xF800));
        var rig = new Rig(withDepth: false);
        rig.Draw(Square(0, 0, 4, 4, shade: 0x0000));
        Assert.Equal(0, rig.Pixel(1, 1));
    }

    [Fact]
    public void ASquareLeaningAwayHasMoreOfTheTextureInItsFarHalf()
    {
        var affine = new Rig(withDepth: false, size: 16);
        affine.Draw(Square(0, 0, 32, 16));
        var leaning = new Rig(withDepth: false, size: 16);
        leaning.Draw(Square(0, 0, 32, 16, zLeft: 100, zRight: 400));
        Assert.Equal(affine.TexelAt(8, 0, 16), affine.Pixel(16, 0));
        int leaningU = Enumerable.Range(0, 16).Single(u => leaning.TexelAt(u, 0, 16) == leaning.Pixel(16, 0));
        Assert.True(leaningU < 6, $"the middle pixel shows u = {leaningU}; perspective puts less than half of the texture in the near half");
        Assert.Equal(leaning.TexelAt(0, 0, 16), leaning.Pixel(0, 0));
        Assert.Equal(leaning.TexelAt(15, 0, 16), leaning.Pixel(31, 0));
    }

    [Fact]
    public void AHiddenPixelReadsNoTexelAndAVisibleOneTakesThreeTicks()
    {
        var rig = new Rig(withDepth: true);
        int nearTicks = rig.Draw(Square(0, 0, 4, 4, zLeft: 100, zRight: 100));
        Assert.Equal(16, rig.Rasterizer.TexelsRead);
        int farTicks = rig.Draw(Square(0, 0, 4, 4, zLeft: 200, zRight: 200));
        Assert.Equal(16, rig.Rasterizer.TexelsRead);
        Assert.Equal(16, rig.Rasterizer.PixelsHidden);
        Assert.Equal(16 * 3, nearTicks - farTicks + 16 * 2);
    }

    [Fact]
    public void TheSizeIsAPowerOfTwoAndItNeedsItsMemories()
    {
        var rig = new Rig(withDepth: false);
        Assert.Throws<ArgumentException>(() => new TexturedRasterizer("T", "t", rig.Host, rig.List, rig.Texture, rig.Video, rig.Screen, null, rig.Triangles, rig.Texels, 24));
        Assert.Throws<ArgumentException>(() => new TexturedRasterizer("T", "t", rig.Host, rig.List, rig.Texture, rig.Video, rig.Screen, null, rig.Triangles, null));
        Assert.Throws<ArgumentException>(() => new TexturedRasterizer("T", "t", rig.Host, rig.List, rig.Texture, rig.Video, null, null, rig.Triangles, rig.Texels));
    }

    private static IEnumerable<int[]> Orders(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        foreach (var first in items)
            foreach (var rest in Orders(items.Where(i => i != first).ToArray()))
                yield return new[] { first }.Concat(rest).ToArray();
    }

    [Fact]
    public void EveryDeviceOrderDrawsTheSamePixels()
    {
        var results = Orders(new[] { 0, 1, 2, 3, 4, 5 }).Select(order =>
        {
            var rig = new Rig(withDepth: true, order: order);
            int ticks = rig.Draw(Square(0, 0, 6, 4, zLeft: 50, zRight: 90).Concat(Square(2, 2, 6, 4, zLeft: 70, zRight: 70)).ToArray());
            var pixels = string.Join(",", Enumerable.Range(0, 10).SelectMany(y => Enumerable.Range(0, 10).Select(x => rig.Pixel(x, y))));
            return $"{ticks} {rig.Rasterizer.TexelsRead} {rig.Rasterizer.PixelsHidden} {pixels}";
        }).Distinct().ToList();
        Assert.Single(results);
    }
}
