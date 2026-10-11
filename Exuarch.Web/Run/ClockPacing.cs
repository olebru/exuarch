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
        public const int FullRecordingHz = 200;
    }

    public sealed class RunFrame
    {
        private readonly Func<bool> tick;
        private readonly Action<bool> recordEveryTick;
        private readonly Func<long> screenSwaps;

        public RunFrame(ElapsedClock clock, Func<bool> tick, Action<bool> recordEveryTick, Func<long> screenSwaps)
        {
            Clock = clock;
            this.tick = tick;
            this.recordEveryTick = recordEveryTick;
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
        public long ScreenSwaps() => screenSwaps();
    }

    public sealed class MaxSpeedPacer : IClockPacer
    {
        private const int Batch = 256;

        public bool RationsRedraws => true;

        public bool RunFrame(RunFrame frame)
        {
            frame.RecordEveryTick(false);
            long swaps = frame.ScreenSwaps();
            bool keepGoing = true;
            while (keepGoing && !frame.ScreenSwapped && frame.Clock.Milliseconds < ClockPacing.FrameMilliseconds)
            {
                for (int i = 0; i < Batch && keepGoing; i++) keepGoing = frame.Tick();
                frame.ScreenSwapped = frame.ScreenSwaps() != swaps;
            }
            frame.OwedTicks = 0;
            frame.RecordEveryTick(true);
            return keepGoing && frame.Tick();
        }

        public int? WaitMilliseconds(double frameMilliseconds) => null;
    }

    public sealed class FixedHzPacer : IClockPacer
    {
        private const double MaxSecondsBehind = 0.25;
        private const int LongestWaitMilliseconds = 16;
        private const int ShortestWaitMilliseconds = 1;

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
            frame.OwedTicks = Math.Min(frame.OwedTicks + hz * frame.SecondsSinceLastFrame, hz * MaxSecondsBehind + 1);
            bool keepGoing = true;
            while (keepGoing && frame.OwedTicks >= (recordEvery ? 1 : 2) && frame.Clock.Milliseconds < ClockPacing.FrameMilliseconds)
            {
                keepGoing = frame.Tick();
                frame.OwedTicks--;
            }
            frame.RecordEveryTick(true);
            if (keepGoing && !recordEvery && frame.OwedTicks >= 1)
            {
                keepGoing = frame.Tick();
                frame.OwedTicks = Math.Max(0, frame.OwedTicks - 1);
            }
            return keepGoing;
        }

        public int? WaitMilliseconds(double frameMilliseconds) => Math.Max(ShortestWaitMilliseconds, Math.Min(LongestWaitMilliseconds, 1000 / Math.Max(1, hz) - (int)frameMilliseconds));
    }
}
