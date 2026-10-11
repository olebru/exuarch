using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class FallingBlocksTests
{
    private const int Well = 3500;
    private sealed class ManualTime : TimeProvider
    {
        public long Milliseconds;
        public override long GetTimestamp() => Milliseconds;
        public override long TimestampFrequency => 1000;
    }
    private readonly ManualTime time = new ManualTime();
    private int ticksPerMs = 250;
    private long ticks;
    private readonly Machine c;
    private readonly Keypad keys;
    private readonly RamModule mem;
    private readonly Framebuffer screen;
    private readonly System.Collections.Generic.Dictionary<string, int> labels;

    public FallingBlocksTests()
    {
        var package = BuiltInPackages.Get("IRQ-16");
        var source = package.Program("Falling blocks").Source;
        c = new Machine(package.Machine, source, DeviceRegistry.CreateDefault(time)) { RecordHistory = false };
        keys = c.Device<Keypad>("keys");
        mem = c.Device<RamModule>("mem");
        screen = c.Device<Framebuffer>("fb");
        labels = new AssemblyLanguage(package.Machine.Decoder.Microcode, 4096, 0).Analyze(source).Labels;
    }

    private int Var(string name) => mem.ValueAt(labels[name]);
    private void Set(string name, int value) => mem.memory[labels[name]] = (ushort)value;
    private int Square(int column, int row) => mem.ValueAt(Well + row * 10 + column);
    private string Lcd => c.Device<CharacterDisplay>("lcd").Text;
    private ushort Pixel(int x, int y) => screen.Pixels[y * Framebuffer.Width + x];

    private void Step()
    {
        c.SingleStep();
        if (++ticks % ticksPerMs == 0) time.Milliseconds++;
    }

    private void Run(int count)
    {
        for (int i = 0; i < count; i++) Step();
        Assert.False(c.IsHalted);
    }

    private void RunUntil(Func<bool> condition, int limit = 5_000_000)
    {
        for (int i = 0; i < limit && !condition(); i++) Step();
        Assert.True(condition(), "the game did not get there. LCD:\n" + Lcd);
        Assert.Empty(c.MicrocodeWarnings);
    }

    private void Tap(Keypad.Keys key)
    {
        keys.Press(key);
        Run(2_000);
        keys.Release(key);
        Run(2_000);
    }

    private void Start()
    {
        RunUntil(() => Lcd.Contains("Space to start"));
        Tap(Keypad.Keys.Space);
        RunUntil(() => Lcd.Contains("SCORE") && Lcd.Contains("space drops"));
    }

    private void Settle() => Run(150_000);

    [Fact]
    public void FitsBelowTheWellInMemory()
    {
        Assert.True(labels.Values.Max() < Well - 8, $"the program reaches {labels.Values.Max()}");
    }

    [Fact]
    public void WaitsForSpaceThenShowsTheScoreAndAPiece()
    {
        Start();
        Assert.Contains("ROWS 000", Lcd);
        Assert.Contains("LEVEL 0", Lcd);
        Settle();
        Assert.InRange(Var("p_piece"), 0, 6);
        Assert.InRange(Var("next"), 0, 6);
        Assert.Equal(0x8410, Pixel(215, 200));
        var colours = new[] { 0x07FF, 0xFFE0, 0xA01F, 0x07E0, 0xF800, 0x3A7F, 0xFC00 };
        int colour = colours[Var("p_piece")];
        int squares = 0;
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 10; column++)
                if (Pixel(220 + column * 20 + 9, 40 + row * 20 + 9) == colour) squares++;
        Assert.Equal(4, squares);
    }

    [Fact]
    public void ThePieceFallsByItself()
    {
        Start();
        Settle();
        int y = Var("p_y");
        RunUntil(() => Var("p_y") == y + 2);
    }

    [Theory]
    [InlineData(250)]
    [InlineData(1000)]
    public void ThePieceFallsARowEvery800MillisecondsAtAnyClockSpeed(int ticksPerMillisecond)
    {
        ticksPerMs = ticksPerMillisecond;
        Start();
        Settle();
        int y = Var("p_y");
        RunUntil(() => Var("p_y") == y + 1, 20_000_000);
        long from = time.Milliseconds;
        RunUntil(() => Var("p_y") == y + 3, 20_000_000);
        Assert.InRange(time.Milliseconds - from, 2 * 800 - 20, 2 * 800 + 20);
    }

    [Fact]
    public void SpaceDropsThePieceToTheBottom()
    {
        Start();
        Settle();
        int colour = new[] { 0x07FF, 0xFFE0, 0xA01F, 0x07E0, 0xF800, 0x3A7F, 0xFC00 }[Var("p_piece")];
        Tap(Keypad.Keys.Space);
        Settle();
        int landed = 0;
        for (int row = 16; row < 20; row++)
            for (int column = 0; column < 10; column++)
                if (Square(column, row) == colour) landed++;
        Assert.Equal(4, landed);
        Assert.True(Enumerable.Range(0, 10).Any(column => Square(column, 19) != 0), "nothing on the bottom row");
    }

    [Fact]
    public void LeftGoesToTheWallAndNoFurther()
    {
        Start();
        Settle();
        keys.Press(Keypad.Keys.Left);
        Run(400_000);
        keys.Release(Keypad.Keys.Left);
        Settle();
        int shape = labels["shapes"] + Var("p_piece") * 32 + Var("p_rot") * 8;
        int left = Enumerable.Range(0, 4).Min(i => mem.ValueAt(shape + 2 * i));
        Assert.Equal(0, (short)(Var("p_x") + left));
    }

    [Fact]
    public void UpTurnsThePiece()
    {
        Start();
        Settle();
        int rot = Var("p_rot");
        Tap(Keypad.Keys.Up);
        Settle();
        Assert.Equal((rot + 1) % 4, Var("p_rot"));
    }

    [Fact]
    public void AFullRowGoesAndScores()
    {
        Start();
        Settle();
        for (int column = 4; column < 10; column++) mem.memory[Well + 190 + column] = 0xF800;
        int squareAboveTheFullRow = Well + 180 + 9;
        mem.memory[squareAboveTheFullRow] = 0x07E0;
        int highestStackRow = 18;
        Set("top", highestStackRow);
        Set("p_piece", 0);
        Set("p_rot", 0);
        Set("p_x", 0);
        Tap(Keypad.Keys.Space);
        RunUntil(() => Lcd.Contains("SCORE 000100") && Lcd.Contains("space drops"));
        Assert.Contains("ROWS 001", Lcd);
        Settle();
        Assert.Equal(0x07E0, Square(9, 19));
        Assert.Equal(0, Square(9, 18));
        Assert.Equal(1, Enumerable.Range(0, 10).Count(column => Square(column, 19) != 0));
        Assert.Equal(0x07E0, Pixel(220 + 9 * 20 + 9, 40 + 19 * 20 + 9));
        Assert.Equal(0, Pixel(220 + 9 * 20 + 9, 40 + 18 * 20 + 9));
    }

    [Fact]
    public void FourRowsAtOnceScore800AndTenRowsRaiseTheLevel()
    {
        const int StandingUpWithSquaresInBoxColumn2 = 1;
        const int TwoLeftOfTheWallToLandInColumn0 = 0xFFFE;
        Start();
        Settle();
        for (int round = 0; round < 3; round++)
        {
            for (int row = 16; row < 20; row++)
                for (int column = 1; column < 10; column++) mem.memory[Well + row * 10 + column] = 0xF800;
            Set("top", 16);
            Set("p_piece", 0);
            Set("p_rot", StandingUpWithSquaresInBoxColumn2);
            Set("p_x", TwoLeftOfTheWallToLandInColumn0);
            Tap(Keypad.Keys.Space);
            int expected = 800 * (round + 1);
            RunUntil(() => Lcd.Contains($"SCORE {expected:000000}") && Lcd.Contains("space drops"));
            Settle();
        }
        Assert.Contains("ROWS 012", Lcd);
        Assert.Contains("LEVEL 1", Lcd);
        Assert.Equal(45, Var("fall_delay"));
    }

    [Fact]
    public void AFullWellEndsTheGameAndSpacePlaysAgain()
    {
        Start();
        Settle();
        for (int row = 2; row < 20; row++)
            for (int column = 0; column < 10; column++)
                if ((row + column) % 3 != 0) mem.memory[Well + row * 10 + column] = 0xF800;
        Set("top", 2);
        Tap(Keypad.Keys.Space);
        RunUntil(() => Lcd.Contains("GAME OVER") && Lcd.Contains("Space plays again"));
        Settle();
        Tap(Keypad.Keys.Space);
        RunUntil(() => !Lcd.Contains("GAME OVER") && Lcd.Contains("SCORE 000000"));
        Settle();
        Assert.All(Enumerable.Range(0, 200).Where(i => i >= 40), i => Assert.Equal(0, mem.ValueAt(Well + i)));
    }
}
