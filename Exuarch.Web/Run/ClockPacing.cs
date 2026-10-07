using System;

namespace Exuarch.Web.Run
{
    // How a run keeps time, one frame at a time: how many ticks the frame runs, whether the view's redraws are rationed,
    // and how long the frame waits before the next one.
    public interface IClockPacer
    {
        // Rationed, the whole view is drawn only now and then, to leave the machine the time (see RedrawThrottle).
        bool RationsRedraws { get; }

        // Runs the frame's ticks: false when one of them stopped the run.
        bool RunFrame(RunFrame frame);

        // How long to wait before the next frame, in milliseconds, or null to only give the browser its turn.
        int? WaitMilliseconds(double frameMilliseconds);
    }

    public static class ClockPacing
    {
        // How long a frame runs ticks before the browser has its turn, which is also how long a click can wait.
        public const int FrameMilliseconds = 25;
        // At or below this speed every tick is recorded, so the trace is complete.
        public const int FullRecordingHz = 200;
    }

    // One frame of a run, as a pacer sees it, and what is carried from frame to frame.
    public sealed class RunFrame
    {
        private readonly Func<bool> tick;
        private readonly Action<bool> recordEveryTick;
        private readonly Func<long> screenSwaps;

        // tick runs one tick and says whether the run may go on; recordEveryTick turns the machine's history on or off;
        // screenSwaps counts the frames the double buffered screens have swapped in so far.
        public RunFrame(ElapsedClock clock, Func<bool> tick, Action<bool> recordEveryTick, Func<long> screenSwaps)
        {
            Clock = clock;
            this.tick = tick;
            this.recordEveryTick = recordEveryTick;
            this.screenSwaps = screenSwaps;
        }

        // Time since the frame started.
        public ElapsedClock Clock { get; }
        public double SecondsSinceLastFrame { get; private set; }
        // Ticks a set speed owes and has not run yet.
        public double Owed { get; set; }
        // A double buffered screen swapped in a finished frame during this one.
        public bool Swapped { get; set; }

        // The frame's clock has just been restarted.
        public void Start(double secondsSinceLastFrame)
        {
            SecondsSinceLastFrame = secondsSinceLastFrame;
            Swapped = false;
        }

        public bool Tick() => tick();
        public void RecordEveryTick(bool every) => recordEveryTick(every);
        public long ScreenSwaps() => screenSwaps();
    }

    // As fast as the browser allows: ticks in batches for a frame's time, or until a double buffered screen swaps in a
    // finished frame so the view can show it, without recording them. Then one more tick that is recorded, the one on
    // screen. The view is redrawn on its own slower beat, and the frame only gives the browser its turn.
    public sealed class MaxSpeedPacer : IClockPacer
    {
        private const int Batch = 256;

        public bool RationsRedraws => true;

        public bool RunFrame(RunFrame frame)
        {
            frame.RecordEveryTick(false);
            long swaps = frame.ScreenSwaps();
            bool keepGoing = true;
            while (keepGoing && !frame.Swapped && frame.Clock.Milliseconds < ClockPacing.FrameMilliseconds)
            {
                for (int i = 0; i < Batch && keepGoing; i++) keepGoing = frame.Tick();
                frame.Swapped = frame.ScreenSwaps() != swaps;
            }
            frame.Owed = 0;
            frame.RecordEveryTick(true);
            return keepGoing && frame.Tick();
        }

        public int? WaitMilliseconds(double frameMilliseconds) => null;
    }

    // At the slider's speed: the ticks due are worked out from the time that has passed, so the clock keeps its rate
    // however long a frame takes to draw. Above FullRecordingHz only the last tick of a frame is recorded, the one on
    // screen, and the others run without the per tick detail.
    public sealed class FixedHzPacer : IClockPacer
    {
        private readonly int hz;

        public FixedHzPacer(int hz)
        {
            this.hz = hz;
        }

        public bool RationsRedraws => false;

        public bool RunFrame(RunFrame frame)
        {
            bool recordEvery = hz <= ClockPacing.FullRecordingHz;
            frame.RecordEveryTick(recordEvery);
            // Never more than a quarter of a second behind, so a slow frame is not made up for with a burst.
            frame.Owed = Math.Min(frame.Owed + hz * frame.SecondsSinceLastFrame, hz * 0.25 + 1);
            bool keepGoing = true;
            while (keepGoing && frame.Owed >= (recordEvery ? 1 : 2) && frame.Clock.Milliseconds < ClockPacing.FrameMilliseconds)
            {
                keepGoing = frame.Tick();
                frame.Owed--;
            }
            frame.RecordEveryTick(true);
            if (keepGoing && !recordEvery && frame.Owed >= 1)
            {
                keepGoing = frame.Tick();
                frame.Owed = Math.Max(0, frame.Owed - 1);
            }
            return keepGoing;
        }

        // Until the next tick is due, at most a sixtieth of a second, and at least a millisecond.
        public int? WaitMilliseconds(double frameMilliseconds) => Math.Max(1, Math.Min(16, 1000 / Math.Max(1, hz) - (int)frameMilliseconds));
    }
}
