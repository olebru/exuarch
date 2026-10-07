using System;

namespace Exuarch.Web.Run
{
    // A stopwatch on a TimeProvider, so a run's timing can be tested with time that only moves when it is told to.
    public sealed class ElapsedClock
    {
        private readonly TimeProvider time;
        private long start;

        // Started.
        public ElapsedClock(TimeProvider time)
        {
            this.time = time;
            start = time.GetTimestamp();
        }

        public void Restart() => start = time.GetTimestamp();

        public double Milliseconds => time.GetElapsedTime(start).TotalMilliseconds;
        public double Seconds => time.GetElapsedTime(start).TotalSeconds;
    }
}
