using System;

namespace Exuarch.Web.Run
{
    public interface IClockPacer
    {
        bool RationsRedraws { get; }

        bool RunFrame(RunFrame frame);

        int? WaitMilliseconds(double frameMilliseconds);
    }

    public static class ClockPacing
    {
        public const int FrameMilliseconds = 25;
        public const int HighestTracedHz = 200_000;
    }

    public sealed class RunFrame
    {
        private readonly Func<bool> tick;
        private readonly Action<bool> recordEveryTick;
        private readonly Action<bool> traceTicks;
        private readonly Func<long> screenSwaps;

        public RunFrame(ElapsedClock clock, Func<bool> tick, Action<bool> recordEveryTick, Action<bool> traceTicks, Func<long> screenSwaps)
        {
            Clock = clock;
            this.tick = tick;
            this.recordEveryTick = recordEveryTick;
            this.traceTicks = traceTicks;
            this.screenSwaps = screenSwaps;
        }

        public ElapsedClock Clock { get; }
        public double SecondsSinceLastFrame { get; private set; }
        public double OwedTicks { get; set; }
        public bool ScreenSwapped { get; set; }

        public void Start(double secondsSinceLastFrame)
        {
            SecondsSinceLastFrame = secondsSinceLastFrame;
            ScreenSwapped = false;
        }

        public bool Tick() => tick();
        public void RecordEveryTick(bool every) => recordEveryTick(every);
        public void TraceTicks(bool traced) => traceTicks(traced);
        public long ScreenSwaps() => screenSwaps();
    }

    public sealed class MaxSpeedPacer : IClockPacer
    {
        private const int Batch = 256;

        public bool RationsRedraws => true;

        public bool RunFrame(RunFrame frame)
        {
            frame.RecordEveryTick(false);
            frame.TraceTicks(false);
            long swaps = frame.ScreenSwaps();
            bool keepGoing = true;
            while (keepGoing && !frame.ScreenSwapped && frame.Clock.Milliseconds < ClockPacing.FrameMilliseconds)
            {
                for (int i = 0; i < Batch && keepGoing; i++) keepGoing = frame.Tick();
                frame.ScreenSwapped = frame.ScreenSwaps() != swaps;
            }
            frame.OwedTicks = 0;
            frame.RecordEveryTick(true);
            keepGoing = keepGoing && frame.Tick();
            frame.TraceTicks(true);
            return keepGoing;
        }

        public int? WaitMilliseconds(double frameMilliseconds) => null;
    }

    public sealed class FixedHzPacer : IClockPacer
    {
        private const int Batch = 64;
        private const double MaxSecondsBehind = 0.25;
        private const int LongestWaitMilliseconds = 16;
        private const int ShortestWaitMilliseconds = 1;

        private readonly int hz;

        public FixedHzPacer(int hz)
        {
            this.hz = hz;
        }

        public bool RationsRedraws => false;

        public bool Traced => hz <= ClockPacing.HighestTracedHz;

        public bool RunFrame(RunFrame frame)
        {
            frame.OwedTicks = Math.Min(frame.OwedTicks + hz * frame.SecondsSinceLastFrame, hz * MaxSecondsBehind + 1);
            frame.RecordEveryTick(Traced);
            frame.TraceTicks(Traced);
            long swaps = frame.ScreenSwaps();
            bool keepGoing = TickInBatches(frame, swaps, Traced ? 0 : 1);
            frame.RecordEveryTick(true);
            if (keepGoing && !Traced && frame.OwedTicks >= 1) keepGoing = TickOwed(frame, 1, swaps);
            frame.TraceTicks(true);
            return keepGoing;
        }

        private static bool TickInBatches(RunFrame frame, long swaps, int keptForTheLastTick)
        {
            bool keepGoing = true;
            while (keepGoing && !frame.ScreenSwapped && frame.OwedTicks >= keptForTheLastTick + 1 && frame.Clock.Milliseconds < ClockPacing.FrameMilliseconds)
            {
                keepGoing = TickOwed(frame, (int)Math.Min(Batch, frame.OwedTicks - keptForTheLastTick), swaps);
            }
            return keepGoing;
        }

        private static bool TickOwed(RunFrame frame, int ticks, long swaps)
        {
            bool keepGoing = true;
            for (int i = 0; i < ticks && keepGoing; i++) keepGoing = frame.Tick();
            frame.OwedTicks = Math.Max(0, frame.OwedTicks - ticks);
            frame.ScreenSwapped = frame.ScreenSwaps() != swaps;
            return keepGoing;
        }

        public int? WaitMilliseconds(double frameMilliseconds) => Math.Max(ShortestWaitMilliseconds, Math.Min(LongestWaitMilliseconds, 1000 / Math.Max(1, hz) - (int)frameMilliseconds));
    }
}
