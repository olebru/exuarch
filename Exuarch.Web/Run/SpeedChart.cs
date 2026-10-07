using System;
using System.Linq;

namespace Exuarch.Web.Run
{
    // The clock panel's chart of the measured speed against the speed the slider asks for.
    public sealed class SpeedChart
    {
        public const double Width = 360, Height = 70;

        private readonly SpeedMeter meter;
        private readonly int hz;

        // At max speed there is no target to show or to make room for.
        public SpeedChart(SpeedMeter meter, int hz, bool max)
        {
            this.meter = meter;
            this.hz = hz;
            Max = Top(Math.Max(meter.Peak, max ? 0 : hz) * 1.1);
        }

        // The chart's top value: a round number above the highest sample and the target.
        public double Max { get; }
        public double Span => meter.Span;
        public double TargetY => Height - Math.Min(1, hz / Max) * Height;

        private static double Top(double top)
        {
            if (top <= 0) return 10;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(top)));
            foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
            {
                if (step * magnitude >= top) return step * magnitude;
            }
            return 10 * magnitude;
        }

        // The samples as SVG points: the line, or the area under it.
        public string Points(bool area)
        {
            var samples = meter.Samples;
            if (samples.Count == 0) return "";
            double end = samples[^1].Seconds, start = end - Span;
            string Point(double seconds, double value) => FormattableString.Invariant($"{(seconds - start) / Span * Width:0.#},{Height - Math.Min(1, value / Max) * Height:0.#}");
            var points = string.Join(" ", samples.Select(s => Point(s.Seconds, s.Hz)));
            return area ? $"{Point(samples[0].Seconds, 0)} {points} {Point(end, 0)}" : points;
        }
    }
}
