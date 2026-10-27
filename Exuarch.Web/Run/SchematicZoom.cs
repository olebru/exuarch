using System;
using Exuarch.Web.Components;

namespace Exuarch.Web.Run
{
    public sealed class SchematicZoom
    {
        private bool fitting = true;
        private double panelWidth;

        public double Zoom { get; private set; } = 1;

        public void StartFitting()
        {
            fitting = true;
        }

        public bool Fit(double width, double contentWidth)
        {
            if (width <= 0) return false;
            panelWidth = width;
            Zoom = FitZoom(width, contentWidth);
            return true;
        }

        public bool Resized(double width, double contentWidth)
        {
            if (width <= 0 || Math.Abs(width - panelWidth) < 1) return false;
            panelWidth = width;
            if (!fitting) return false;
            var fitted = FitZoom(width, contentWidth);
            if (fitted == Zoom) return false;
            Zoom = fitted;
            return true;
        }

        public void By(double factor)
        {
            fitting = false;
            Zoom = ZoomLimits.By(Zoom, factor);
        }

        public ZoomChange ByWheelSteps(int steps)
        {
            var before = Zoom;
            fitting = false;
            Zoom = ZoomLimits.ByWheelSteps(Zoom, steps);
            return new ZoomChange(before, Zoom);
        }

        private static double FitZoom(double width, double contentWidth) => Math.Clamp(Math.Floor((width - 24) / contentWidth * 100) / 100, ZoomLimits.Min, 1.0);
    }
}
