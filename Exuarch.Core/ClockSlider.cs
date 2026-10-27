using System;
namespace Exuarch.Core
{
    public static class ClockSlider
    {
        public const int Steps = 1000;
        public const int MaxHz = 2_000_000;

        public static int HzAt(int step)
        {
            double hz = Math.Pow(MaxHz, step / (double)Steps);
            if (hz < 100) return (int)Math.Round(hz);
            double unit = Math.Pow(10, Math.Floor(Math.Log10(hz)) - 1);
            return (int)(Math.Round(hz / unit) * unit);
        }

        public static int? StepAtLeast(int hz)
        {
            for (int step = LowestCandidateStep(hz); step <= Steps; step++)
            {
                if (HzAt(step) >= hz) return step;
            }
            return null;
        }

        public static int? StepFor(int hz)
        {
            return StepAtLeast(hz) is int step && HzAt(step) == hz ? step : null;
        }

        public static int Nearest(int hz)
        {
            int above = StepAtLeast(hz) ?? Steps;
            int below = Math.Max(0, above - 1);
            return Math.Abs(HzAt(above) - hz) <= Math.Abs(hz - HzAt(below)) ? HzAt(above) : HzAt(below);
        }

        private static int LowestCandidateStep(int hz)
        {
            return Math.Max(0, (int)Math.Floor(Math.Log(Math.Max(1, hz)) / Math.Log(MaxHz) * Steps) - 1);
        }
    }
}
