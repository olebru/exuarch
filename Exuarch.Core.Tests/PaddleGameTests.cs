using System;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class PaddleGameTests
{
    private readonly Machine c;
    private readonly Keypad keys;
    private readonly RamModule data;
    private readonly Framebuffer screen;

    public PaddleGameTests()
    {
        var package = BuiltInPackages.Get("HARVARD-16");
        c = new Machine(package.Machine, package.Program("Paddle game").Source) { RecordHistory = false };
        keys = c.Device<Keypad>("keys");
        data = c.Device<RamModule>("dmem");
        screen = c.Device<Framebuffer>("fb");
    }

    private int BallX => data.ValueAt(0);
    private int BallY => data.ValueAt(1);
    private int PaddleX => data.ValueAt(4);
    private string Lcd => c.Device<CharacterDisplay>("lcd").Text;
    private ushort Pixel(int x, int y) => screen.Pixels[y * Framebuffer.Width + x];

    private void RunUntil(Func<bool> condition, int limit = 3_000_000)
    {
        for (int i = 0; i < limit && !condition(); i++) c.SingleStep();
        Assert.True(condition(), "the game did not get there");
        Assert.Empty(c.MicrocodeWarnings);
    }

    [Fact]
    public void DrawsThePaddleAndShowsTheScore()
    {
        RunUntil(() => BallY > 40);
        Assert.Contains("Arrows move the paddle", Lcd);
        Assert.Contains("Score 00", Lcd);
        for (int x = 288; x < 352; x += 7) Assert.Equal(0xFFFF, Pixel(x, 444));
        Assert.Equal(0, Pixel(287, 444));
        Assert.Equal(0, Pixel(352, 444));
    }

    [Fact]
    public void TheBallMovesDiagonallyAndIsDrawnWhereItIs()
    {
        RunUntil(() => BallY == 60);
        Assert.Equal(340, BallX);
        RunUntil(() => BallY == 64 && data.ValueAt(12) == 0xFFE0);
        RunUntil(() => data.ValueAt(12) == 0);
        Assert.Equal(0xFFE0, Pixel(BallX, BallY));
        Assert.Equal(0xFFE0, Pixel(BallX + 7, BallY + 7));
        Assert.Equal(0, Pixel(BallX - 4, BallY - 4));
    }

    [Fact]
    public void BouncesOffTheRightWall()
    {
        RunUntil(() => BallX == 632);
        RunUntil(() => BallX == 628);
        Assert.Equal(0xFFFC, data.ValueAt(2));
    }

    [Fact]
    public void LeftMovesThePaddleToTheWallAndNoFurther()
    {
        RunUntil(() => BallY > 40);
        keys.Press(Keypad.Keys.Left);
        RunUntil(() => PaddleX == 0);
        for (int i = 0; i < 20_000; i++) c.SingleStep();
        Assert.Equal(0, PaddleX);
        for (int x = 0; x < 64; x += 7) Assert.Equal(0xFFFF, Pixel(x, 444));
        Assert.Equal(0, Pixel(64, 444));
    }

    [Fact]
    public void RightMovesThePaddleToTheWallAndNoFurther()
    {
        RunUntil(() => BallY > 40);
        keys.Press(Keypad.Keys.Right);
        RunUntil(() => PaddleX == 576);
        for (int i = 0; i < 20_000; i++) c.SingleStep();
        Assert.Equal(576, PaddleX);
        for (int x = 576; x < 640; x += 7) Assert.Equal(0xFFFF, Pixel(x, 444));
        Assert.Equal(0, Pixel(575, 444));
    }

    [Fact]
    public void ThePaddleBouncesTheBallAndScores()
    {
        for (int frame = 0; frame < 4000 && !Lcd.Contains("Score 01"); frame++)
        {
            var target = BallX - 28;
            keys.ReleaseAll();
            if (PaddleX > target + 4) keys.Press(Keypad.Keys.Left);
            else if (PaddleX + 4 < target) keys.Press(Keypad.Keys.Right);
            for (int i = 0; i < 400; i++) c.SingleStep();
            Assert.DoesNotContain("Game over", Lcd);
        }
        Assert.Contains("Score 01", Lcd);
        Assert.Equal(0xFFFC, data.ValueAt(3));
    }

    [Fact]
    public void AMissEndsTheGameAndSpaceStartsANewOne()
    {
        keys.Press(Keypad.Keys.Left);
        RunUntil(() => Lcd.Contains("Game over"));
        keys.ReleaseAll();
        for (int i = 0; i < 20_000; i++) c.SingleStep();
        Assert.Contains("Game over", Lcd);

        keys.Press(Keypad.Keys.Space);
        keys.Release(Keypad.Keys.Space);
        RunUntil(() => !Lcd.Contains("Game over"));
        RunUntil(() => BallY > 40);
        Assert.Equal(288, PaddleX);
        Assert.Contains("Score 00", Lcd);
    }
}
