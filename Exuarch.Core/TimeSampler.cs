using System;
namespace Exuarch.Core
{
    internal sealed class TimeSampler
    {
        public const int MostTicksBetweenSamples = 65536;
        private readonly TimeProvider time;
        private long lastLook;
        private int ticksBetweenSamples = 1, ticksToSample = 1;

        public TimeSampler(TimeProvider time)
        {
            this.time = time;
        }
        public int TicksBetweenSamples { get { return ticksBetweenSamples; } }

        public long Restart()
        {
            lastLook = time.GetTimestamp();
            ticksBetweenSamples = ticksToSample = 1;
            return lastLook;
        }

        public bool Look(out long now)
        {
            now = 0;
            if (--ticksToSample > 0) return false;
            now = time.GetTimestamp();
            long quarter = Math.Max(1, time.TimestampFrequency / 4000), took = now - lastLook;
            ticksBetweenSamples = took <= 0
                ? Math.Min(ticksBetweenSamples * 2, MostTicksBetweenSamples)
                : (int)Math.Clamp(ticksBetweenSamples * quarter / took, 1, MostTicksBetweenSamples);
            ticksToSample = ticksBetweenSamples;
            lastLook = now;
            return true;
        }
    }
}
