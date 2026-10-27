using System;
using System.Collections.Generic;

namespace Exuarch.Web.Devices
{
    public sealed class FrameRate
    {
        public const double WindowSeconds = 1;

        private readonly TimeProvider time;
        private readonly Queue<(long At, long Total)> samples = new Queue<(long, long)>();

        public FrameRate(TimeProvider time)
        {
            this.time = time;
        }

        public double? PerSecond(long total)
        {
            long now = time.GetTimestamp();
            samples.Enqueue((now, total));
            while (samples.Count > 1 && time.GetElapsedTime(samples.Peek().At, now).TotalSeconds > WindowSeconds) samples.Dequeue();
            var (oldestAt, oldestTotal) = samples.Peek();
            double seconds = time.GetElapsedTime(oldestAt, now).TotalSeconds;
            return seconds > 0 ? (total - oldestTotal) / seconds : null;
        }

        public void Clear() => samples.Clear();
    }
}
