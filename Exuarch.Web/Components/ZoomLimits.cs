using System;

namespace Exuarch.Web.Components
{
    public static class ZoomLimits
    {
        public const double Min = 0.4;
        public const double Max = 1.6;

        public static double By(double zoom, double factor) => Math.Clamp(Math.Round(zoom * factor, 2), Min, Max);
    }
}
