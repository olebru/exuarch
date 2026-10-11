using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Hardware
{
    public sealed class CanvasInteraction
    {
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

        public async Task Leave()
        {
            var gesture = Gesture;
            Gesture = null;
            if (gesture != null) await gesture.Cancel();
        }

        public void Drop()
        {
            Gesture = null;
        }
    }
}
