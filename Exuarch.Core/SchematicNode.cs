using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    // Which card: a device by its id, or the decoder.
    internal readonly record struct NodeId(string Kind, string Id)
    {
        public static readonly NodeId Decoder = new NodeId("decoder", "decoder");
        public static NodeId Device(string id) => new NodeId("device", id);
        // As callers name a card: kind "decoder" is the decoder whatever the id.
        public static NodeId Of(string kind, string id) => kind == "decoder" ? Decoder : new NodeId(kind, id);
    }

    // A card on the schematic, a device or the decoder, as placement, routing and the canvas see it: where it is, how
    // tall it is, which buses its ports are on and which devices its sockets name.
    internal abstract class SchematicNode
    {
        public abstract NodeId Id { get; }
        // Null until the card has been placed.
        public abstract Position Layout { get; set; }
        public abstract double Height { get; }
        // The bus each port is on.
        public abstract IEnumerable<string> BusIds { get; }
        // The device each socket names, in socket order; null for an empty socket.
        public abstract IReadOnlyList<string> SocketTargets { get; }
        // Everything the card is wired to by name, as the machine definition keeps it.
        public abstract IEnumerable<(string Name, string Target)> Wiring { get; }

        public bool Placed => Layout != null;
        public Rect Bounds => new Rect(Layout.X, Layout.Y, SchematicLayout.CardWidth, Height);
        // The devices it is wired to.
        public IEnumerable<string> Links => Wiring.Select(w => w.Target).Where(t => t != null);
        // Where a socket's wire leaves the card: on its right edge, level with the socket's row.
        public Point SocketPoint(int index) => new Point(Layout.X + SchematicLayout.CardWidth, Layout.Y + SchematicLayout.SocketRowY(index));

        public virtual bool Is(DeviceDefinition device) => false;

        // Every card of a machine, placed or not: the devices in order, then the decoder.
        public static IEnumerable<SchematicNode> Of(MachineDefinition definition, DeviceRegistry registry)
        {
            foreach (var device in definition.Devices) yield return new DeviceNode(device, registry.InfoFor(device));
            if (definition.Decoder != null) yield return new DecoderNode(definition.Decoder);
        }
    }

    internal sealed class DeviceNode : SchematicNode
    {
        public DeviceNode(DeviceDefinition device, DeviceTypeInfo info)
        {
            Device = device;
            Info = info;
        }

        public DeviceDefinition Device { get; }
        public DeviceTypeInfo Info { get; }

        public override NodeId Id => NodeId.Device(Device.Id);
        public override Position Layout { get => Device.Layout; set => Device.Layout = value; }
        public override double Height => SchematicLayout.HeightFor(Device, Info);
        public override IEnumerable<string> BusIds => Device.Ports().Select(p => p.Value);
        public override IReadOnlyList<string> SocketTargets => Info.Connections.Select(c => Device.Connections.GetValueOrDefault(c.Name)).ToList();
        public override IEnumerable<(string Name, string Target)> Wiring => Device.Connections.Select(c => (c.Key, c.Value));
        public override bool Is(DeviceDefinition device) => device == Device;
    }

    // The decoder's card, whose sockets name the status register, the step counter and the interrupt controller.
    internal sealed class DecoderNode : SchematicNode
    {
        private readonly DecoderDefinition decoder;

        public DecoderNode(DecoderDefinition decoder)
        {
            this.decoder = decoder;
        }

        // The device a socket of the decoder names.
        public static string Target(DecoderDefinition decoder, string socket)
        {
            return socket switch
            {
                "status" => decoder?.Status,
                "instructionRegister" => decoder?.InstructionRegister,
                _ => decoder?.Interrupts,
            };
        }

        public override NodeId Id => NodeId.Decoder;
        public override Position Layout { get => decoder.Layout; set => decoder.Layout = value; }
        public override double Height => SchematicLayout.DecoderHeight;
        public override IEnumerable<string> BusIds => Enumerable.Empty<string>();
        public override IReadOnlyList<string> SocketTargets => Wiring.Select(w => w.Target).ToList();
        public override IEnumerable<(string Name, string Target)> Wiring => SchematicLayout.DecoderSockets.Select(s => (s.Name, Target(decoder, s.Name)));
    }
}
