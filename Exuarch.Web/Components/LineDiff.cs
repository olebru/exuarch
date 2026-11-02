using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Web.Components
{
    public enum DiffKind
    {
        Same,
        Removed,
        Added,
        Skipped,
    }

    public sealed record DiffLine(DiffKind Kind, string Text, int BeforeNumber, int AfterNumber, int SkippedCount = 0);

    public sealed record Changes(string Title, string BeforeLabel, string AfterLabel, string Before, string After)
    {
        public IReadOnlyList<DiffLine> Lines => LineDiff.WithContext(LineDiff.Compare(Before, After), LineDiff.ContextLines);
    }

    public static class LineDiff
    {
        public const int ContextLines = 3;
        private const long MaxTableCells = 4_000_000;

        public static IReadOnlyList<DiffLine> Compare(string before, string after)
        {
            var a = SplitLines(before);
            var b = SplitLines(after);
            int head = 0;
            while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
            int tail = 0;
            while (tail < a.Length - head && tail < b.Length - head && a[a.Length - 1 - tail] == b[b.Length - 1 - tail]) tail++;

            var lines = new List<DiffLine>();
            for (int i = 0; i < head; i++) lines.Add(new DiffLine(DiffKind.Same, a[i], i + 1, i + 1));
            lines.AddRange(CompareMiddle(a, b, head, a.Length - tail, b.Length - tail));
            for (int i = tail; i > 0; i--) lines.Add(new DiffLine(DiffKind.Same, a[a.Length - i], a.Length - i + 1, b.Length - i + 1));
            return lines;
        }

        public static IReadOnlyList<DiffLine> WithContext(IReadOnlyList<DiffLine> lines, int context)
        {
            var near = new bool[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Kind == DiffKind.Same) continue;
                for (int j = Math.Max(0, i - context); j <= Math.Min(lines.Count - 1, i + context); j++) near[j] = true;
            }
            var shown = new List<DiffLine>();
            for (int i = 0; i < lines.Count;)
            {
                if (near[i])
                {
                    shown.Add(lines[i++]);
                    continue;
                }
                int start = i;
                while (i < lines.Count && !near[i]) i++;
                shown.Add(new DiffLine(DiffKind.Skipped, "", lines[start].BeforeNumber, lines[start].AfterNumber, i - start));
            }
            return shown;
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            return text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        }

        private static IEnumerable<DiffLine> CompareMiddle(string[] a, string[] b, int head, int aEnd, int bEnd)
        {
            int n = aEnd - head;
            int m = bEnd - head;
            if ((long)n * m > MaxTableCells) return Replaced(a, b, head, aEnd, bEnd);
            var common = CommonLengths(a, b, head, n, m);
            var lines = new List<DiffLine>();
            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[head + x] == b[head + y])
                {
                    lines.Add(new DiffLine(DiffKind.Same, a[head + x], head + x + 1, head + y + 1));
                    x++;
                    y++;
                }
                else if (y < m && (x == n || common[x, y + 1] >= common[x + 1, y]))
                {
                    lines.Add(new DiffLine(DiffKind.Added, b[head + y], 0, head + y + 1));
                    y++;
                }
                else
                {
                    lines.Add(new DiffLine(DiffKind.Removed, a[head + x], head + x + 1, 0));
                    x++;
                }
            }
            return RemovedBeforeAdded(lines);
        }

        private static int[,] CommonLengths(string[] a, string[] b, int head, int n, int m)
        {
            var common = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    common[i, j] = a[head + i] == b[head + j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            return common;
        }

        private static IEnumerable<DiffLine> Replaced(string[] a, string[] b, int head, int aEnd, int bEnd)
        {
            return Enumerable.Range(head, aEnd - head).Select(i => new DiffLine(DiffKind.Removed, a[i], i + 1, 0))
                .Concat(Enumerable.Range(head, bEnd - head).Select(j => new DiffLine(DiffKind.Added, b[j], 0, j + 1)));
        }

        private static List<DiffLine> RemovedBeforeAdded(List<DiffLine> lines)
        {
            var ordered = new List<DiffLine>();
            for (int i = 0; i < lines.Count;)
            {
                if (lines[i].Kind == DiffKind.Same)
                {
                    ordered.Add(lines[i++]);
                    continue;
                }
                int start = i;
                while (i < lines.Count && lines[i].Kind != DiffKind.Same) i++;
                var run = lines.GetRange(start, i - start);
                ordered.AddRange(run.Where(l => l.Kind == DiffKind.Removed));
                ordered.AddRange(run.Where(l => l.Kind == DiffKind.Added));
            }
            return ordered;
        }
    }
}
