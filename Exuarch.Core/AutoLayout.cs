using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    // Automatic placement as a pipeline of stages, a reduced Sugiyama layout: who is wired to whom, which row each card
    // goes in, the order of the cards along the rows, their columns, and how tall the rows and the gaps between the
    // buses are. A machine without buses gets a plain grid instead.
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

    // Nothing to arrange around: the devices in a grid six wide, the decoder to the right of it.
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

    // One card as placement sees it. The rows: 2 * bus is the row above that bus, 2 * bus + 1 the row below it.
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
        // The indexes of the buses its ports are on, lowest first.
        public List<int> Buses { get; }
        public List<PlacementCard> Partners { get; } = new List<PlacementCard>();
        // Other rows this card's wires pass through, where its column has to stay free.
        public List<int> Through { get; set; } = new List<int>();
        public int Row { get; set; }
        public int Column { get; set; }
        // Where it wants to go along its row, as a column.
        public double Key { get; set; }

        // On two or more buses: it sits in a gap between two of them.
        public bool Bridges => Buses.Count > 1;
        // On no bus, as the decoder is: nothing to line up with, so it goes at the end of the top row.
        public bool Parked => Buses.Count == 0;
    }

    // The cards with the buses they are on and who is wired to whom, either way round.
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

    // Rows first. A card on one bus goes above or below it, towards the cards it is wired to; the rest are shared out
    // so the two rows stay even. A card on more buses goes in a gap between them. Cards on no bus go in the top row.
    internal sealed class RowAssigner
    {
        // How far the partners' buses have to lie above or below before a card follows them.
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
            // The first bus's top row also holds the decoder and devices on no bus.
            return bus == 0 && lean >= 0 && Count(0) > Count(1);
        }

        // In the gap between two of its buses: the one nearest the middle of where its ports and partners are.
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

        // The rows a wire from the gap passes on its way: up to a bus above, down to one below.
        private static IEnumerable<int> Passed(int bus, int gap, int row)
        {
            return bus <= gap ? Enumerable.Range(2 * bus + 1, row - (2 * bus + 1)) : Enumerable.Range(row + 1, 2 * bus - row);
        }
    }

    // The order along the rows: each card starts where it comes in its row, then a few rounds of moving each card
    // towards the average column of its partners.
    internal static class BarycentreOrdering
    {
        private const int Rounds = 4;
        // Past every column: where parked cards go.
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

    // Columns in key order: cards that reach through other rows first, each in the leftmost column that is free in
    // every row it needs, then each row's other cards left to right.
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

    // Which columns are taken in which rows.
    internal sealed class OccupancyGrid
    {
        private readonly HashSet<(int Row, int Column)> taken = new HashSet<(int Row, int Column)>();

        private bool Free(PlacementCard card, int column) => !taken.Contains((card.Row, column)) && card.Through.All(r => !taken.Contains((r, column)));

        // The first column from the one given that is free in every row the card needs.
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

    // Heights: each gap between two buses is as tall as its two rows, so the buses and the tops of the rows follow.
    internal sealed class RowGeometry
    {
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
                // A bus with nothing above it still leaves room at the top of the drawing.
                busY[b] = rowHeight[2 * b] > 0 ? y + rowHeight[2 * b] + SchematicPlacement.ToBus : y + (b == 0 ? 60 : 0);
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
