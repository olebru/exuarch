using System;
namespace Exuarch.Core
{
    // Decides in which ticks a device reads the time. Reading the time costs more than a tick does, so it is read
    // about every quarter of a millisecond of real time: from how long the ticks since the last look took, the
    // sampler works out how many ticks that is. At 16 Hz that is every tick; at 2 MHz, every few hundred.
    internal sealed class TimeSampler
    {
        public const int MostTicksBetweenLooks = 65536;
        private readonly TimeProvider time;
        private long lastLook;
        private int ticksBetweenLooks = 1, ticksToLook = 1;

        public TimeSampler(TimeProvider time)
        {
            this.time = time;
        }
        public int TicksBetweenLooks { get { return ticksBetweenLooks; } }

        // Looks now, and from the next tick on every tick until it has learnt how fast the ticks come. Returns the time.
        public long Restart()
        {
            lastLook = time.GetTimestamp();
            ticksBetweenLooks = ticksToLook = 1;
            return lastLook;
        }

        // Called once a tick: true, with the time, in the ticks that look.
        public bool Look(out long now)
        {
            now = 0;
            if (--ticksToLook > 0) return false;
            now = time.GetTimestamp();
            long quarter = Math.Max(1, time.TimestampFrequency / 4000), took = now - lastLook;
            ticksBetweenLooks = took <= 0
                ? Math.Min(ticksBetweenLooks * 2, MostTicksBetweenLooks)
                : (int)Math.Clamp(ticksBetweenLooks * quarter / took, 1, MostTicksBetweenLooks);
            ticksToLook = ticksBetweenLooks;
            lastLook = now;
            return true;
        }
    }
}
