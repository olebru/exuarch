using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

// Plays IRQ-16's falling blocks by pressing keys on the keypad and watching the well in memory, the screen and the LCD.
// The real time clock reads a clock moved by hand: one millisecond every ticksPerMs ticks, 250 for a 250 kHz machine.
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

    // A key held for long enough for the main loop to read it, then let go.
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

    // Waits until the main loop is back reading keys with no move half done.
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
        // The grey frame round the well, and the piece's colour in the top rows.
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

    // The real time clock keeps the pace: a row every 0.8 seconds at level 0 whether the machine runs at 250 kHz
    // or four times as fast.
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
        // The piece's leftmost square is in column 0: x plus the smallest column in its shape.
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
        // A bottom row full but for columns 0 to 3, and a straight piece, lying flat, above them.
        for (int column = 4; column < 10; column++) mem.memory[Well + 190 + column] = 0xF800;
        mem.memory[Well + 180 + 9] = 0x07E0;   // one square above, which moves down when the row goes
        Set("top", 18);                         // the game's note of the stack's highest row
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
        // And the screen shows the square that moved down.
        Assert.Equal(0x07E0, Pixel(220 + 9 * 20 + 9, 40 + 19 * 20 + 9));
        Assert.Equal(0, Pixel(220 + 9 * 20 + 9, 40 + 18 * 20 + 9));
    }

    [Fact]
    public void FourRowsAtOnceScore800AndTenRowsRaiseTheLevel()
    {
        Start();
        Settle();
        for (int round = 0; round < 3; round++)
        {
            for (int row = 16; row < 20; row++)
                for (int column = 1; column < 10; column++) mem.memory[Well + row * 10 + column] = 0xF800;
            Set("top", 16);
            Set("p_piece", 0);
            Set("p_rot", 1);       // standing up: the squares are in the box's column 2
            Set("p_x", 0xFFFE);    // two left of the wall, so they land in column 0
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
