using System;

namespace Exuarch.Web.Run
{
    public sealed class ElapsedClock
    {
        private readonly TimeProvider time;
        private long start;

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
