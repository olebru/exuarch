using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Hardware
{
    // The pointer on the design canvas: the zoom, and the gesture in progress. The editor measures the canvas in the
    // browser; this turns pointer positions in the window into canvas coordinates and hands them to the gesture.
    public sealed class CanvasInteraction
    {
        // Actual size until the viewer zooms.
        public double Zoom { get; private set; } = 1;
        public CanvasGesture Gesture { get; private set; }

        public bool Dragging => Gesture != null;
        public bool IsLayoutDraft => Gesture?.IsLayoutDraft == true;
        public RenderFragment Sketch => Gesture?.Sketch;
        public RenderFragment Ghost => Gesture?.Ghost;
        public bool Highlights(BusDefinition bus) => Gesture?.Highlights(bus) == true;
        public bool Targets(DeviceDefinition device) => Gesture?.Targets(device) == true;

        public void ZoomBy(double factor)
        {
            Zoom = ZoomLimits.By(Zoom, factor);
        }

        public void Begin(CanvasGesture gesture, double clientX, double clientY, string undoSnapshot)
        {
            gesture.Start(clientX, clientY, undoSnapshot);
            Gesture = gesture;
        }
        // Where the canvas turned out to be in the window; the pointer is followed from then on.
        public void Place(CanvasGesture gesture, ElementRect canvas, double clientX, double clientY)
        {
            gesture.Place(canvas);
            gesture.Track(clientX, clientY, Zoom);
        }

        public void Move(double clientX, double clientY)
        {
            var gesture = Gesture;
            if (gesture == null || !gesture.HasRect) return;
            gesture.Track(clientX, clientY, Zoom);
            if (!gesture.Moved) return;
            var (dx, dy) = gesture.Offset(clientX, clientY, Zoom);
            gesture.Move(dx, dy);
        }

        public async Task Release(double clientX, double clientY, SchematicLayout layout)
        {
            var gesture = Gesture;
            Gesture = null;
            if (gesture == null || !gesture.HasRect) return;
            gesture.Track(clientX, clientY, Zoom);
            bool onCanvas = gesture.X >= 0 && gesture.Y >= 0 && gesture.X <= layout.CanvasWidth && gesture.Y <= layout.CanvasHeight;
            await gesture.Complete(onCanvas);
        }

        // Leaving the editor gives the gesture up.
        public async Task Leave()
        {
            var gesture = Gesture;
            Gesture = null;
            if (gesture != null) await gesture.Cancel();
        }

        // Forgets the gesture, leaving whatever it did so far (Escape).
        public void Drop()
        {
            Gesture = null;
        }
    }
}
