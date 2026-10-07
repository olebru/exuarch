using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Hardware
{
    // A drag on the design canvas, from the pointer going down until it comes up or leaves the editor. Each kind is its
    // own class: what it selects, what moving the pointer does, what letting go does and what it draws meanwhile.
    public abstract class CanvasGesture
    {
        private double startX, startY;
        private double canvasLeft, canvasTop;

        protected CanvasGesture(MachineDesign design)
        {
            Design = design;
        }

        protected MachineDesign Design { get; }
        // The definition as it was when the pointer went down.
        public string UndoSnapshot { get; private set; }
        // Where the pointer is on the canvas, once the canvas has been measured.
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool HasRect { get; private set; }
        // Whether the pointer has gone far enough to be a drag rather than a click.
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
        // Follows the pointer, given where it is in the browser window.
        public void Track(double clientX, double clientY, double zoom)
        {
            X = (clientX - canvasLeft) / zoom;
            Y = (clientY - canvasTop) / zoom;
            if (Math.Abs(clientX - startX) + Math.Abs(clientY - startY) > 4) Moved = true;
        }
        // How far the pointer is from where it went down, in canvas units.
        public (double Dx, double Dy) Offset(double clientX, double clientY, double zoom)
        {
            return ((clientX - startX) / zoom, (clientY - startY) / zoom);
        }

        // What the pointer going down selects, if anything.
        public virtual Selection Picks => null;
        // The pointer moved, by (dx, dy) from where it went down.
        public virtual void Move(double dx, double dy) { }
        // The pointer came up; onCanvas tells whether that was over the canvas.
        public abstract Task Complete(bool onCanvas);
        // The pointer left the editor before coming up.
        public virtual Task Cancel() => Task.CompletedTask;
        // While cards are moved the wires are quick curves; they are routed again once the drag is over.
        public virtual bool IsLayoutDraft => false;
        // Drawn on the wires layer while the gesture lasts, and over the cards.
        public virtual RenderFragment Sketch => null;
        public virtual RenderFragment Ghost => null;
        // A bus or device lit up as the place the gesture would connect to.
        public virtual bool Highlights(BusDefinition bus) => false;
        public virtual bool Targets(DeviceDefinition device) => false;
    }

    // Moving a card or a bus: the definition changes as the pointer moves, and is kept, as one step in the history,
    // when the pointer comes up. Leaving the editor puts it back where it was.
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

        public override Selection Picks => new DeviceSelection(deviceId);

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

        public override Selection Picks => new BusSelection(busId);

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

        public override Selection Picks => Selection.Decoder;

        public override void Move(double dx, double dy)
        {
            var layout = Design.Definition.Decoder.Layout;
            layout.X = Math.Max(0, MachineDesign.Snap(originX + dx));
            layout.Y = Math.Max(0, MachineDesign.Snap(originY + dy));
        }
    }

    // A device type dragged from the palette onto the canvas, or clicked there.
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
