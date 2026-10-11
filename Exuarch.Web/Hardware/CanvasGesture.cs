using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Hardware
{
    public abstract class CanvasGesture
    {
        private const double MinimumDragDistance = 4;
        private double startX, startY;
        private double canvasLeft, canvasTop;

        protected CanvasGesture(MachineDesign design)
        {
            Design = design;
        }

        protected MachineDesign Design { get; }
        public string UndoSnapshot { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool HasRect { get; private set; }
        public bool Moved { get; private set; }

        public void Start(double clientX, double clientY, string undoSnapshot)
        {
            startX = clientX;
            startY = clientY;
            UndoSnapshot = undoSnapshot;
        }
        public void Place(ElementRect canvas)
        {
            canvasLeft = canvas.Left;
            canvasTop = canvas.Top;
            HasRect = true;
        }
        public void Track(double clientX, double clientY, double zoom)
        {
            X = (clientX - canvasLeft) / zoom;
            Y = (clientY - canvasTop) / zoom;
            if (Math.Abs(clientX - startX) + Math.Abs(clientY - startY) > MinimumDragDistance) Moved = true;
        }
        public (double Dx, double Dy) Offset(double clientX, double clientY, double zoom)
        {
            return ((clientX - startX) / zoom, (clientY - startY) / zoom);
        }

        public virtual Selection SelectionOnStart => null;
        public virtual void Move(double dx, double dy) { }
        public abstract Task Complete(bool onCanvas);
        public virtual Task Cancel() => Task.CompletedTask;
        public virtual bool IsLayoutDraft => false;
        public virtual RenderFragment Sketch => null;
        public virtual RenderFragment Ghost => null;
        public virtual bool Highlights(BusDefinition bus) => false;
        public virtual bool Targets(DeviceDefinition device) => false;
    }

    public abstract class MoveGesture : CanvasGesture
    {
        protected MoveGesture(MachineDesign design) : base(design) { }

        public override bool IsLayoutDraft => Moved;

        public override async Task Complete(bool onCanvas)
        {
            if (Moved) await Design.Commit(UndoSnapshot);
        }
        public override async Task Cancel()
        {
            if (Moved) await Design.Replace(MachineDefinition.FromJson(UndoSnapshot));
        }
    }

    public sealed class MoveDeviceGesture : MoveGesture
    {
        private readonly string deviceId;
        private readonly double originX, originY;

        public MoveDeviceGesture(MachineDesign design, DeviceDefinition device) : base(design)
        {
            deviceId = device.Id;
            originX = device.Layout.X;
            originY = device.Layout.Y;
        }

        public override Selection SelectionOnStart => new DeviceSelection(deviceId);

        public override void Move(double dx, double dy)
        {
            var device = Design.Definition.FindDevice(deviceId);
            device.Layout.X = Math.Max(0, MachineDesign.Snap(originX + dx));
            device.Layout.Y = Math.Max(0, MachineDesign.Snap(originY + dy));
        }
    }

    public sealed class MoveBusGesture : MoveGesture
    {
        private readonly string busId;
        private readonly double originY;

        public MoveBusGesture(MachineDesign design, BusDefinition bus) : base(design)
        {
            busId = bus.Id;
            originY = bus.Layout.Y;
        }

        public override Selection SelectionOnStart => new BusSelection(busId);

        public override void Move(double dx, double dy)
        {
            Design.Definition.FindBus(busId).Layout.Y = Math.Max(20, MachineDesign.Snap(originY + dy));
        }
    }

    public sealed class MoveDecoderGesture : MoveGesture
    {
        private readonly double originX, originY;

        public MoveDecoderGesture(MachineDesign design) : base(design)
        {
            originX = design.Definition.Decoder.Layout.X;
            originY = design.Definition.Decoder.Layout.Y;
        }

        public override Selection SelectionOnStart => Selection.Decoder;

        public override void Move(double dx, double dy)
        {
            var layout = Design.Definition.Decoder.Layout;
            layout.X = Math.Max(0, MachineDesign.Snap(originX + dx));
            layout.Y = Math.Max(0, MachineDesign.Snap(originY + dy));
        }
    }

    public sealed class NewDeviceGesture : CanvasGesture
    {
        private readonly string type;

        public NewDeviceGesture(MachineDesign design, string type) : base(design)
        {
            this.type = type;
        }

        public override Task Complete(bool onCanvas)
        {
            if (Moved && onCanvas) return Design.AddDevice(type, X - SchematicLayout.CardWidth / 2, Y - 20);
            return Moved ? Task.CompletedTask : Design.AddDevice(type);
        }

        public override RenderFragment Ghost => Moved ? DesignCanvas.GhostCard(type, X, Y) : null;
    }
}
