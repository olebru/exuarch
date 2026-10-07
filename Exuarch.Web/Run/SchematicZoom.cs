using System;
using Exuarch.Web.Components;

namespace Exuarch.Web.Run
{
    // How big the live drawing is shown. It is shown at its actual size, 100%, and only zoomed out as far as it takes to
    // fit a machine that is wider than its panel; it is never zoomed in by itself. This holds whenever the panel changes
    // width, through the window or a column's handle, until the viewer zooms by hand. Clicking the percentage goes back
    // to it.
    public sealed class SchematicZoom
    {
        private bool fitting = true;
        private double panelWidth;

        public double Zoom { get; private set; } = 1;

        // Fits from now on, once the panel's width is known.
        public void StartFitting()
        {
            fitting = true;
        }

        // The panel is this wide: false when that says nothing, as before the panel is laid out.
        public bool Fit(double width, double contentWidth)
        {
            if (width <= 0) return false;
            panelWidth = width;
            Zoom = FitZoom(width, contentWidth);
            return true;
        }

        // The panel changed width: true when the zoom changed with it.
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

        // Zooming by hand, which stops the fitting.
        public void By(double factor)
        {
            fitting = false;
            Zoom = ZoomLimits.By(Zoom, factor);
        }

        private static double FitZoom(double width, double contentWidth) => Math.Clamp(Math.Floor((width - 24) / contentWidth * 100) / 100, ZoomLimits.Min, 1.0);
    }
}
