using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Components
{
    public sealed record ControlWire(string DeviceId, string Line, string Color, string Path, double DecoderPinX, double DevicePinX, double DevicePinY);

    public sealed record ControlTarget(DeviceDefinition Device, string Color, List<string> Lines);

    public sealed class ControlWiring
    {
        public const double PinSpacing = 6;
        public const double TrackSpacing = 3;
        public const double RibbonSpacing = 2;
        private const double PinMargin = 8;
        private const double Clearance = 8;
        private const double TopMargin = 34;
        private const double GutterWidth = 22;

        private readonly MachineDefinition definition;
        private readonly SchematicLayout layout;

        public ControlWiring(MachineDefinition definition, SchematicLayout layout)
        {
            this.definition = definition;
            this.layout = layout;
        }

        public static List<ControlTarget> TargetsOf(MachineDefinition definition, Func<DeviceDefinition, DeviceTypeInfo> info, Func<string, IBusDevice> built)
        {
            return definition.Devices.Where(d => d.Layout != null)
                .OrderBy(d => d.Layout.X).ThenBy(d => d.Layout.Y)
                .Select(d => new ControlTarget(d, Palette.Category(info(d)), SignalResolver.LinesOf(info(d), built(d.Id)) ?? new List<string>()))
                .Where(t => t.Lines.Count > 0)
                .ToList();
        }

        public List<ControlWire> Route(List<ControlTarget> targets)
        {
            var decoder = definition.Decoder?.Layout;
            if (decoder == null || targets.Count == 0) return new List<ControlWire>();

            var decoderBottom = decoder.Y + SchematicLayout.DecoderHeight;
            var total = targets.Sum(t => t.Lines.Count);
            var decoderPins = PinsAcross(decoder.X, total);
            var lanes = new LaneAllocator(this, decoderBottom, targets);

            var wires = new List<ControlWire>();
            int global = 0;
            foreach (var target in targets)
            {
                var device = target.Device;
                var pins = PinsAcross(device.Layout.X, target.Lines.Count);
                var climb = Math.Min(RibbonSpacing, TopMargin / target.Lines.Count);
                var gutter = Math.Min(RibbonSpacing, GutterWidth / target.Lines.Count);
                var enterFromRight = device.Layout.X < decoder.X;
                for (int i = 0; i < target.Lines.Count; i++, global++)
                {
                    var sx = decoderPins[global];
                    var px = pins[i];
                    var top = device.Layout.Y;
                    var track = lanes.Next(device);
                    string path;
                    if (top > track)
                    {
                        path = FormattableString.Invariant($"M {sx:0.##} {decoderBottom:0.##} V {track:0.##} H {px:0.##} V {top:0.##}");
                    }
                    else
                    {
                        var nesting = enterFromRight ? target.Lines.Count - 1 - i : i;
                        var column = enterFromRight ? device.Layout.X + SchematicLayout.CardWidth + 4 + nesting * gutter : device.Layout.X - 4 - nesting * gutter;
                        var over = top - 4 - nesting * climb;
                        path = FormattableString.Invariant($"M {sx:0.##} {decoderBottom:0.##} V {track:0.##} H {column:0.##} V {over:0.##} H {px:0.##} V {top:0.##}");
                    }
                    wires.Add(new ControlWire(device.Id, target.Lines[i], target.Color, path, sx, px, top));
                }
            }
            return wires;
        }

        public double DecoderBottom => definition.Decoder?.Layout == null ? 0 : definition.Decoder.Layout.Y + SchematicLayout.DecoderHeight;

        private static double[] PinsAcross(double left, int count)
        {
            var spacing = Math.Min(PinSpacing, (SchematicLayout.CardWidth - 2 * PinMargin) / Math.Max(1, count - 1));
            var first = left + SchematicLayout.CardWidth / 2 - spacing * (count - 1) / 2;
            return Enumerable.Range(0, count).Select(i => first + i * spacing).ToArray();
        }

        private double BusAbove(double y, double floor) => definition.Buses.Where(b => b.Layout != null && b.Layout.Y < y).Select(b => b.Layout.Y).DefaultIfEmpty(floor).Max();

        private double BusBelow(double y, double ceiling) => definition.Buses.Where(b => b.Layout != null && b.Layout.Y > y).Select(b => b.Layout.Y).DefaultIfEmpty(ceiling).Min();

        private sealed class LaneAllocator
        {
            private readonly ControlWiring wiring;
            private readonly double decoderBottom;
            private readonly Dictionary<double, (double Start, double Step, int Used)> zones = new Dictionary<double, (double, double, int)>();

            public LaneAllocator(ControlWiring wiring, double decoderBottom, List<ControlTarget> targets)
            {
                this.wiring = wiring;
                this.decoderBottom = decoderBottom;
                foreach (var group in targets.GroupBy(t => ZoneKey(t.Device)))
                {
                    var count = group.Sum(t => t.Lines.Count);
                    zones[group.Key] = Zone(group.Key, count);
                }
            }

            public double Next(DeviceDefinition device)
            {
                var key = ZoneKey(device);
                var zone = zones[key];
                zones[key] = zone with { Used = zone.Used + 1 };
                return zone.Start + zone.Used * zone.Step;
            }

            private double ZoneKey(DeviceDefinition device) => device.Layout.Y > decoderBottom + Clearance ? device.Layout.Y : double.NegativeInfinity;

            private (double Start, double Step, int Used) Zone(double key, int count)
            {
                if (double.IsNegativeInfinity(key))
                {
                    var start = decoderBottom + Clearance;
                    var end = wiring.BusBelow(decoderBottom, decoderBottom + 120) - Clearance;
                    return (start, Math.Min(TrackSpacing, (end - start) / Math.Max(1, count)), 0);
                }
                var bottom = key - Clearance;
                var top = Math.Max(wiring.BusAbove(key, decoderBottom), decoderBottom) + Clearance;
                var step = Math.Min(TrackSpacing, (bottom - top) / Math.Max(1, count));
                return (bottom - (count - 1) * step, step, 0);
            }
        }
    }
}
