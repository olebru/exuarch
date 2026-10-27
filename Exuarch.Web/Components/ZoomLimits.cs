using System;

namespace Exuarch.Web.Components
{
    public readonly record struct ZoomChange(double Before, double After);

    public static class ZoomLimits
    {
        public const double Min = 0.4;
        public const double Max = 1.6;
        public const double WheelStepFactor = 1.1;

        public static double By(double zoom, double factor) => Math.Clamp(Math.Round(zoom * factor, 2), Min, Max);

        public static double ByWheelSteps(double zoom, int steps) => By(zoom, Math.Pow(WheelStepFactor, steps));
    }
}
