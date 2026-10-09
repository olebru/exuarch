using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    // Where the cards go when a machine is laid out automatically. Every bus gets at most one row of cards above it
    // and one below, so a card's port wire runs straight to its bus without passing another card. A device on two or
    // more buses sits in the gap between them, and the column it is in is kept free in the rows its wires pass. Within
    // the rows, devices are ordered to sit near the ones they are connected to, and the gaps between buses are as tall
    // as the rows in them need. The stages are in AutoLayout.
    public static class SchematicPlacement
    {
        public const double Left = 30;
        public const double Top = 40;
        public const double ColumnSpacing = 210;
        // From a card's edge to the bus it faces, and between the two rows that share the gap between two buses.
        public const double ToBus = 56;
        public const double BetweenRows = 70;
        // How far a new card stays from other cards and from the buses.
        private const double Clearance = 20;
        private const int MaxColumns = 64;

        private static readonly Lazy<DeviceRegistry> registry = new Lazy<DeviceRegistry>(() => DeviceRegistry.CreateDefault());

        public static void LayOut(MachineDefinition definition)
        {
            AutoLayout.LayOut(definition, registry.Value);
        }

        // A spot for one new card next to its bus, clear of every card already placed: the first free column in the row
        // above the bus or the row below it, or below everything when there is none.
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

        // The tops of the rows a new card may go in: above and below the first of its buses that has been drawn, or
        // level with the highest card.
        private static List<double> Rows(MachineDefinition definition, DeviceDefinition device, double height, List<Rect> placed)
        {
            var busId = device.Ports().Select(p => p.Value).FirstOrDefault(b => definition.FindBus(b)?.Layout != null);
            if (busId == null) return new List<double> { placed.Count == 0 ? Top : placed.Min(p => p.Y) };
            var bus = definition.FindBus(busId).Layout.Y;
            return new List<double> { bus - ToBus - height, bus + ToBus };
        }

        // Column by column, each row in turn.
        private static IEnumerable<Position> Spots(List<double> rows)
        {
            for (int column = 0; column < MaxColumns; column++)
            {
                foreach (var y in rows.Where(r => r >= 0)) yield return new Position { X = Left + column * ColumnSpacing, Y = y };
            }
        }
    }
}
