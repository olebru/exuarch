using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Exuarch.Core
{
    public enum RomTableKind { Sine, Cosine, Ramp, Squares }

    public static class RomTables
    {
        private const int ValuesPerLine = 8;

        public static readonly IReadOnlyList<(RomTableKind Kind, string Name, string Formula)> Kinds = new[]
        {
            (RomTableKind.Sine, "Sine", "offset + scale × sin(360° × i / count)"),
            (RomTableKind.Cosine, "Cosine", "offset + scale × cos(360° × i / count)"),
            (RomTableKind.Ramp, "Ramp", "offset + scale × i / count"),
            (RomTableKind.Squares, "Squares", "offset + scale × i × i / count"),
        };

        public static int[] Values(RomTableKind kind, int count, int scale, int offset)
        {
            return Enumerable.Range(0, Math.Max(0, count)).Select(i => Word(offset + Value(kind, i, count, scale))).ToArray();
        }

        private static double Value(RomTableKind kind, int i, int count, int scale)
        {
            double turn = 2 * Math.PI * i / count;
            return kind switch
            {
                RomTableKind.Sine => scale * Math.Sin(turn),
                RomTableKind.Cosine => scale * Math.Cos(turn),
                RomTableKind.Ramp => (double)scale * i / count,
                _ => (double)scale * i * i / count,
            };
        }

        private const int LowestLiteral = -32768;

        private static int Word(double value)
        {
            var rounded = (long)Math.Round(value, MidpointRounding.AwayFromZero);
            return rounded >= LowestLiteral && rounded <= Bus.Mask ? (int)rounded : (int)(rounded & Bus.Mask);
        }

        public static List<string> Lines(int[] values)
        {
            var lines = new List<string>();
            var indent = new string(' ', 8);
            for (int start = 0; start < values.Length; start += ValuesPerLine)
            {
                var cells = string.Join(", ", values.Skip(start).Take(ValuesPerLine).Select(v => v.ToString(CultureInfo.InvariantCulture)));
                lines.Add($"{indent}.DATA {cells}");
            }
            return lines;
        }
    }
}
