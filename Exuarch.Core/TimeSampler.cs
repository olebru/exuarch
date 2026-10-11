using System;
namespace Exuarch.Core
{
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

        public long Restart()
        {
            lastLook = time.GetTimestamp();
            ticksBetweenLooks = ticksToLook = 1;
            return lastLook;
        }

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
