using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Web.Run
{
    // The clock speed measured while running: a sample every quarter of a second of running time, kept for a minute.
    public sealed class SpeedMeter
    {
        public const double SampleSeconds = 0.25;
        public const double WindowSeconds = 60;

        // (seconds of running time, ticks per second), oldest first.
        private readonly List<(double Seconds, double Hz)> samples = new List<(double, double)>();
        private double runningSeconds;
        // The run being measured: the time of its last sample, and the machine's cycle count then.
        private double sampleAt;
        private int sampleCycles;

        public IReadOnlyList<(double Seconds, double Hz)> Samples => samples;
        public bool HasSamples => samples.Count > 0;
        public double Current => samples.Count == 0 ? 0 : samples[^1].Hz;
        public double Peak => samples.Count == 0 ? 0 : samples.Max(s => s.Hz);
        // Seconds across the chart: grows with the run until it reaches the window.
        public double Span => Math.Min(WindowSeconds, Math.Max(5, runningSeconds));

        public void Clear()
        {
            samples.Clear();
            runningSeconds = 0;
        }

        public void StartRun(int cycles)
        {
            sampleAt = 0;
            sampleCycles = cycles;
        }

        // A frame of the run ended at now, seconds into it: a sample when one is due.
        public void Sample(double now, int cycles)
        {
            if (now - sampleAt < SampleSeconds) return;
            Add((cycles - sampleCycles) / (now - sampleAt), now - sampleAt);
            sampleAt = now;
            sampleCycles = cycles;
        }

        // The run stopped at now: the time since the last sample counts too, unless it was too short to say anything.
        public void FinishRun(double now, int cycles)
        {
            double elapsed = now - sampleAt;
            if (elapsed > 0.05) Add((cycles - sampleCycles) / elapsed, elapsed);
        }

        private void Add(double hz, double seconds)
        {
            runningSeconds += seconds;
            samples.Add((runningSeconds, hz));
            while (samples.Count > 0 && samples[0].Seconds < runningSeconds - WindowSeconds) samples.RemoveAt(0);
        }
    }
}
