using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    internal static class AutoLayout
    {
        public static void LayOut(MachineDefinition definition, DeviceRegistry registry)
        {
            if (definition.Buses.Count == 0)
            {
                GridFallbackLayout.LayOut(definition);
                return;
            }
            var cards = ConnectivityGraph.From(definition.Buses, SchematicNode.Of(definition, registry));
            new RowAssigner().Assign(cards);
            BarycentreOrdering.Order(cards);
            RowGeometry.For(cards, definition.Buses.Count).Apply(definition.Buses, cards);
        }
    }

    internal static class GridFallbackLayout
    {
        private const int Columns = 6;
        private const double RowSpacing = 140;

        public static void LayOut(MachineDefinition definition)
        {
            int i = 0;
            foreach (var device in definition.Devices)
            {
                device.Layout = new Position { X = SchematicPlacement.Left + (i % Columns) * SchematicPlacement.ColumnSpacing, Y = SchematicPlacement.Top + (i / Columns) * RowSpacing };
                i++;
            }
            if (definition.Decoder != null) definition.Decoder.Layout = new Position { X = SchematicPlacement.Left + Columns * SchematicPlacement.ColumnSpacing, Y = SchematicPlacement.Top };
        }
    }

    internal sealed class PlacementCard
    {
        public PlacementCard(SchematicNode node, int order, List<int> buses)
        {
            Node = node;
            Order = order;
            Buses = buses;
        }

        public SchematicNode Node { get; }
        public int Order { get; }
        public List<int> Buses { get; }
        public List<PlacementCard> Partners { get; } = new List<PlacementCard>();
        public List<int> Through { get; set; } = new List<int>();
        public int Row { get; set; }
        public int Column { get; set; }
        public double Key { get; set; }

        public bool Bridges => Buses.Count > 1;
        public bool Parked => Buses.Count == 0;
    }

    internal static class ConnectivityGraph
    {
        public static List<PlacementCard> From(IReadOnlyList<BusDefinition> buses, IEnumerable<SchematicNode> nodes)
        {
            var busIndex = buses.Select((b, i) => (b.Id, i)).ToDictionary(b => b.Id, b => b.i);
            var cards = nodes.Select((n, i) => new PlacementCard(n, i, BusesOf(n, busIndex))).ToList();
            var byId = cards.ToDictionary(c => c.Node.Id);
            foreach (var card in cards)
            {
                foreach (var target in card.Node.Links) Link(card, byId.GetValueOrDefault(NodeId.Device(target)));
            }
            return cards;
        }

        private static List<int> BusesOf(SchematicNode node, Dictionary<string, int> busIndex)
        {
            return node.BusIds.Where(busIndex.ContainsKey).Select(b => busIndex[b]).Distinct().OrderBy(b => b).ToList();
        }

        private static void Link(PlacementCard a, PlacementCard b)
        {
            if (b == null || a == b) return;
            if (!a.Partners.Contains(b)) a.Partners.Add(b);
            if (!b.Partners.Contains(a)) b.Partners.Add(a);
        }
    }

    internal sealed class RowAssigner
    {
        private const double Lean = 0.25;
        private readonly Dictionary<int, int> perRow = new Dictionary<int, int>();

        public void Assign(IEnumerable<PlacementCard> cards)
        {
            foreach (var card in cards.OrderBy(c => c.Bridges ? 0 : 1).ThenBy(c => c.Order))
            {
                card.Row = card.Parked ? 0 : card.Bridges ? BridgeRow(card) : BusRow(card);
                perRow[card.Row] = Count(card.Row) + 1;
            }
        }

        private int Count(int row) => perRow.GetValueOrDefault(row);

        private int BusRow(PlacementCard card)
        {
            int bus = card.Buses[0];
            var partnerBuses = card.Partners.SelectMany(p => p.Buses).ToList();
            double lean = partnerBuses.Count == 0 ? 0 : partnerBuses.Average() - bus;
            return 2 * bus + (GoesBelow(bus, lean) ? 1 : 0);
        }

        private bool GoesBelow(int bus, double lean)
        {
            if (lean > Lean) return true;
            if (lean < -Lean) return false;
            if (Count(2 * bus + 1) < Count(2 * bus)) return true;
            return bus == 0 && lean >= 0 && Count(0) > Count(1);
        }

        private static int BridgeRow(PlacementCard card)
        {
            var weights = card.Buses.Concat(card.Partners.SelectMany(p => p.Buses)).OrderBy(b => b).ToList();
            double middle = (weights[(weights.Count - 1) / 2] + weights[weights.Count / 2]) / 2.0;
            int first = card.Buses[0], last = card.Buses[^1];
            int gap = Enumerable.Range(first, last - first).OrderBy(g => Math.Abs(g + 0.5 - middle)).First();
            int row = 2 * gap + 1;
            card.Through = card.Buses.SelectMany(bus => Passed(bus, gap, row)).Distinct().ToList();
            return row;
        }

        private static IEnumerable<int> Passed(int bus, int gap, int row)
        {
            return bus <= gap ? Enumerable.Range(2 * bus + 1, row - (2 * bus + 1)) : Enumerable.Range(row + 1, 2 * bus - row);
        }
    }

    internal static class BarycentreOrdering
    {
        private const int Rounds = 4;
        private const double AtTheEnd = 1e9;

        public static void Order(IReadOnlyList<PlacementCard> cards)
        {
            foreach (var row in cards.GroupBy(c => c.Row))
            {
                int index = 0;
                foreach (var card in row.OrderBy(c => c.Order)) card.Key = index++;
            }
            for (int round = 0; round < Rounds; round++)
            {
                ColumnAllocator.Assign(cards);
                foreach (var card in cards) card.Key = card.Partners.Count == 0 ? card.Column : (card.Column + card.Partners.Average(p => (double)p.Column)) / 2;
                foreach (var card in cards.Where(c => c.Parked)) card.Key = AtTheEnd;
            }
            ColumnAllocator.Assign(cards);
        }
    }

    internal static class ColumnAllocator
    {
        private const int MaxStart = 64;

        public static void Assign(IReadOnlyList<PlacementCard> cards)
        {
            var grid = new OccupancyGrid();
            foreach (var card in cards.Where(c => c.Through.Count > 0).OrderBy(c => c.Key).ThenBy(c => c.Order))
            {
                grid.Take(card, grid.FirstFree(card, Math.Max(0, (int)Math.Round(Math.Min(card.Key, MaxStart)))));
            }
            foreach (var row in cards.Where(c => c.Through.Count == 0).GroupBy(c => c.Row))
            {
                int column = 0;
                foreach (var card in row.OrderBy(c => c.Key).ThenBy(c => c.Order))
                {
                    column = grid.FirstFree(card, column);
                    grid.Take(card, column);
                    column++;
                }
            }
        }
    }

    internal sealed class OccupancyGrid
    {
        private readonly HashSet<(int Row, int Column)> taken = new HashSet<(int Row, int Column)>();

        private bool Free(PlacementCard card, int column) => !taken.Contains((card.Row, column)) && card.Through.All(r => !taken.Contains((r, column)));

        public int FirstFree(PlacementCard card, int column)
        {
            while (!Free(card, column)) column++;
            return column;
        }

        public void Take(PlacementCard card, int column)
        {
            card.Column = column;
            taken.Add((card.Row, column));
            foreach (var row in card.Through) taken.Add((row, column));
        }
    }

    internal sealed class RowGeometry
    {
        private const double RoomAboveEmptyTopRow = 60;
        private readonly double[] busY;
        private readonly double[] rowTop;

        private RowGeometry(double[] busY, double[] rowTop)
        {
            this.busY = busY;
            this.rowTop = rowTop;
        }

        public static RowGeometry For(IReadOnlyList<PlacementCard> cards, int busCount)
        {
            var rowHeight = new double[2 * busCount];
            foreach (var card in cards) rowHeight[card.Row] = Math.Max(rowHeight[card.Row], card.Node.Height);
            var busY = new double[busCount];
            var rowTop = new double[2 * busCount];
            double y = SchematicPlacement.Top;
            for (int b = 0; b < busCount; b++)
            {
                rowTop[2 * b] = y;
                busY[b] = rowHeight[2 * b] > 0 ? y + rowHeight[2 * b] + SchematicPlacement.ToBus : y + (b == 0 ? RoomAboveEmptyTopRow : 0);
                rowTop[2 * b + 1] = busY[b] + SchematicPlacement.ToBus;
                y = rowHeight[2 * b + 1] > 0 ? rowTop[2 * b + 1] + rowHeight[2 * b + 1] + SchematicPlacement.BetweenRows : busY[b] + SchematicPlacement.ToBus + SchematicPlacement.BetweenRows / 2;
            }
            return new RowGeometry(busY, rowTop);
        }

        public void Apply(IReadOnlyList<BusDefinition> buses, IEnumerable<PlacementCard> cards)
        {
            for (int b = 0; b < buses.Count; b++) buses[b].Layout = new Position { X = 0, Y = busY[b] };
            foreach (var card in cards)
            {
                card.Node.Layout = new Position { X = SchematicPlacement.Left + card.Column * SchematicPlacement.ColumnSpacing, Y = rowTop[card.Row] };
            }
        }
    }
}
