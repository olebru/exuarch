using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Web.Run
{
    public sealed class SpeedMeter
    {
        public const double SampleSeconds = 0.25;
        public const double WindowSeconds = 60;
        private const double ShortestSpanSeconds = 5;
        private const double ShortestFinalSampleSeconds = 0.05;

        private readonly List<(double Seconds, double Hz)> samples = new List<(double, double)>();
        private double runningSeconds;
        private double sampleAt;
        private int sampleCycles;

        public IReadOnlyList<(double Seconds, double Hz)> Samples => samples;
        public bool HasSamples => samples.Count > 0;
        public double Current => samples.Count == 0 ? 0 : samples[^1].Hz;
        public double Peak => samples.Count == 0 ? 0 : samples.Max(s => s.Hz);
        public double Span => Math.Min(WindowSeconds, Math.Max(ShortestSpanSeconds, runningSeconds));

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

        public void Sample(double now, int cycles)
        {
            if (now - sampleAt < SampleSeconds) return;
            Add((cycles - sampleCycles) / (now - sampleAt), now - sampleAt);
            sampleAt = now;
            sampleCycles = cycles;
        }

        public void FinishRun(double now, int cycles)
        {
            double elapsed = now - sampleAt;
            if (elapsed > ShortestFinalSampleSeconds) Add((cycles - sampleCycles) / elapsed, elapsed);
        }

        private void Add(double hz, double seconds)
        {
            runningSeconds += seconds;
            samples.Add((runningSeconds, hz));
            while (samples.Count > 0 && samples[0].Seconds < runningSeconds - WindowSeconds) samples.RemoveAt(0);
        }
    }
}
