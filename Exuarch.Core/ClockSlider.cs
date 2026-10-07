using System;
namespace Exuarch.Core
{
    // The Run view's clock speed slider. It runs from 1 Hz to MaxHz on a log scale, so each step is the same factor
    // faster and the speeds from 1 Hz to 1 kHz have as much room on it as those from 1 kHz to 1 MHz. Speeds are
    // rounded to two significant figures, so they read as 140 Hz or 12 kHz. Not every such speed has a step: near the
    // top of each decade the steps are further apart than the rounding, so 99 Hz or 980 kHz can not be set.
    public static class ClockSlider
    {
        public const int Steps = 1000;
        public const int MaxHz = 1_000_000;

        // The speed at a slider step, 0 to Steps.
        public static int HzAt(int step)
        {
            double hz = Math.Pow(MaxHz, step / (double)Steps);
            if (hz < 100) return (int)Math.Round(hz);
            double unit = Math.Pow(10, Math.Floor(Math.Log10(hz)) - 1);
            return (int)(Math.Round(hz / unit) * unit);
        }

        // The first step at or above hz, or null when the slider does not go that fast.
        public static int? StepAtLeast(int hz)
        {
            for (int step = FirstStepToTry(hz); step <= Steps; step++)
            {
                if (HzAt(step) >= hz) return step;
            }
            return null;
        }

        // The step for exactly hz, or null when the slider can not be set to it.
        public static int? StepFor(int hz)
        {
            return StepAtLeast(hz) is int step && HzAt(step) == hz ? step : null;
        }

        // The speed the slider can be set to that is closest to hz.
        public static int Nearest(int hz)
        {
            int above = StepAtLeast(hz) ?? Steps;
            int below = Math.Max(0, above - 1);
            return Math.Abs(HzAt(above) - hz) <= Math.Abs(hz - HzAt(below)) ? HzAt(above) : HzAt(below);
        }

        private static int FirstStepToTry(int hz)
        {
            return Math.Max(0, (int)Math.Floor(Math.Log(Math.Max(1, hz)) / Math.Log(MaxHz) * Steps) - 1);
        }
    }
}
