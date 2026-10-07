using System;
using System.Threading.Tasks;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Hardware
{
    // A device's port dragged onto a bus: the bus near the pointer lights up, and letting go there wires the port to it.
    public sealed class WirePortGesture : CanvasGesture
    {
        private readonly string deviceId;
        private readonly string port;

        public WirePortGesture(MachineDesign design, DeviceDefinition device, string port) : base(design)
        {
            deviceId = device.Id;
            this.port = port;
        }

        public override Selection Picks => new DeviceSelection(deviceId);

        public override bool Highlights(BusDefinition bus) => Design.BusNear(Y) == bus;

        public override Task Complete(bool onCanvas)
        {
            var bus = Design.BusNear(Y);
            var device = Design.Definition.FindDevice(deviceId);
            return bus != null && device.GetPortBus(port) != bus.Id ? Design.SetPort(device, port, bus.Id) : Task.CompletedTask;
        }

        public override RenderFragment Sketch
        {
            get
            {
                if (!Moved) return null;
                var anchor = Design.Layout.PortAnchor(Design.Definition.FindDevice(deviceId), port);
                return DesignCanvas.RubberLine(anchor.X, anchor.Y, X, Y);
            }
        }
    }

    // A device's connection socket dragged onto another device, which it then names.
    public sealed class WireConnectionGesture : CanvasGesture
    {
        private readonly string deviceId;
        private readonly string connection;

        public WireConnectionGesture(MachineDesign design, DeviceDefinition device, string connection) : base(design)
        {
            deviceId = device.Id;
            this.connection = connection;
        }

        public override Selection Picks => new DeviceSelection(deviceId);

        public override bool Targets(DeviceDefinition device) => Moved && Design.DeviceAt(X, Y) == device && device.Id != deviceId;

        public override Task Complete(bool onCanvas)
        {
            var target = Design.DeviceAt(X, Y);
            var source = Design.Definition.FindDevice(deviceId);
            return target != null && target != source ? Design.SetConnection(source, connection, target.Id) : Task.CompletedTask;
        }

        public override RenderFragment Sketch
        {
            get
            {
                if (!Moved) return null;
                var device = Design.Definition.FindDevice(deviceId);
                var index = Design.Info(device).Connections.FindIndex(c => c.Name == connection);
                return DesignCanvas.RubberCurve(SchematicLayout.CurveTo(device.Layout.X + SchematicLayout.CardWidth, device.Layout.Y + SchematicLayout.SocketRowY(index), X, Y));
            }
        }
    }

    // One of the decoder's sockets dragged onto the device it should work with.
    public sealed class WireDecoderGesture : CanvasGesture
    {
        private readonly string socket;

        public WireDecoderGesture(MachineDesign design, string socket) : base(design)
        {
            this.socket = socket;
        }

        public override Selection Picks => Selection.Decoder;

        public override bool Targets(DeviceDefinition device) => Moved && Design.DeviceAt(X, Y) == device && MachineDesign.FitsDecoderSocket(socket, device);

        public override Task Complete(bool onCanvas)
        {
            var target = Design.DeviceAt(X, Y);
            return target != null && MachineDesign.FitsDecoderSocket(socket, target) && Design.Layout.DecoderTarget(socket) != target.Id
                ? Design.SetDecoderTarget(socket, target.Id)
                : Task.CompletedTask;
        }

        public override RenderFragment Sketch
        {
            get
            {
                if (!Moved) return null;
                var layout = Design.Definition.Decoder.Layout;
                var index = Array.FindIndex(SchematicLayout.DecoderSockets, s => s.Name == socket);
                return DesignCanvas.RubberCurve(SchematicLayout.CurveTo(layout.X + SchematicLayout.CardWidth, layout.Y + SchematicLayout.SocketRowY(index), X, Y));
            }
        }
    }
}
