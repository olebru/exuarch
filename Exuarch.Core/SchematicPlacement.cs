using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    public static class SchematicPlacement
    {
        public const double Left = 30;
        public const double Top = 40;
        public const double ColumnSpacing = 210;
        public const double ToBus = 56;
        public const double BetweenRows = 70;
        private const double Clearance = 20;
        private const int MaxColumns = 64;

        private static readonly Lazy<DeviceRegistry> registry = new Lazy<DeviceRegistry>(() => DeviceRegistry.CreateDefault());

        public static void LayOut(MachineDefinition definition)
        {
            AutoLayout.LayOut(definition, registry.Value);
        }

        public static Position FreeSpot(MachineDefinition definition, DeviceDefinition device)
        {
            var placed = SchematicNode.Of(definition, registry.Value).Where(n => n.Placed && !n.Is(device)).Select(n => n.Bounds).ToList();
            var busYs = definition.Buses.Where(b => b.Layout != null).Select(b => b.Layout.Y).ToList();
            var obstacles = new Obstacles(placed, busYs);
            double height = SchematicLayout.HeightFor(device, registry.Value.InfoFor(device));
            return Spots(Rows(definition, device, height, placed))
                .FirstOrDefault(spot => obstacles.IsClear(new Rect(spot.X, spot.Y, SchematicLayout.CardWidth, height), Clearance))
                ?? new Position { X = Left, Y = placed.Select(p => p.Bottom).Concat(busYs).DefaultIfEmpty(Top).Max() + ToBus };
        }

        private static List<double> Rows(MachineDefinition definition, DeviceDefinition device, double height, List<Rect> placed)
        {
            var busId = device.Ports().Select(p => p.Value).FirstOrDefault(b => definition.FindBus(b)?.Layout != null);
            if (busId == null) return new List<double> { placed.Count == 0 ? Top : placed.Min(p => p.Y) };
            var bus = definition.FindBus(busId).Layout.Y;
            return new List<double> { bus - ToBus - height, bus + ToBus };
        }

        private static IEnumerable<Position> Spots(List<double> rows)
        {
            for (int column = 0; column < MaxColumns; column++)
            {
                foreach (var y in rows.Where(r => r >= 0)) yield return new Position { X = Left + column * ColumnSpacing, Y = y };
            }
        }
    }
}
