using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class RealTimeClock : ControlLineDevice, IBusDevice, IInterruptSource, IObservableState
    {
        public int Interval { get; private set; }
        public bool Running { get; private set; }
        public long Expired { get; private set; }
        public double Elapsed { get { return Running ? time.GetElapsedTime(since).TotalMilliseconds : 0; } }

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private readonly TimeProvider time;
        private readonly TimeSampler sampler;
        private long since;
        private bool start, stop, output, request;
        private readonly LatchedLines loads = new LatchedLines();

        public RealTimeClock(string DeviceName, string DeviceID, Bus bus, int interval, TimeProvider time = null)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            Interval = interval;
            this.time = time ?? TimeProvider.System;
            sampler = new TimeSampler(this.time);
            ControlLines
                .Add("loadinterval", loads.Add(() => Interval = bus.Data))
                .Add("start", () => start = true)
                .Add("stop", () => stop = true)
                .Add("output", () => output = true);
        }

        public bool TakeInterruptRequest()
        {
            var taken = request;
            request = false;
            return taken;
        }

        public void Drive()
        {
            if (output)
            {
                bus.Data = Interval;
                output = false;
            }
        }
        public void Latch()
        {
            loads.Latch();
            if (start)
            {
                Running = true;
                since = sampler.Restart();
            }
            if (stop) Running = false;
            else if (Running && !start && Interval > 0 && sampler.Look(out long now)) RequestInterruptIfIntervalPassed(now);
            start = stop = false;
        }
        private void RequestInterruptIfIntervalPassed(long now)
        {
            long interval = Interval * time.TimestampFrequency / 1000;
            if (now - since < interval) return;
            bool missedWholeIntervals = now - since >= 2 * interval;
            since = missedWholeIntervals ? now : since + interval;
            Expired++;
            request = true;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
        public void Observe(WatchValue watch) { watch(deviceID + ".expired", () => (int)Expired); }
    }
}
