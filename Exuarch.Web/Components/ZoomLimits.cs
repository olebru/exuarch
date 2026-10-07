using System;

namespace Exuarch.Web.Components
{
    // How far the drawings of a machine zoom, on the design canvas and in the Run view: from 40% to 160%.
    public static class ZoomLimits
    {
        public const double Min = 0.4;
        public const double Max = 1.6;

        // A zoom changed by hand by a factor, to a whole percent and within the limits.
        public static double By(double zoom, double factor) => Math.Clamp(Math.Round(zoom * factor, 2), Min, Max);
    }
}
