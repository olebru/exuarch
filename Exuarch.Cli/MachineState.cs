using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class MemoryDump
    {
        public string Name { get; set; }
        public int Start { get; set; }
        public int Count { get; set; }
        public Func<int, int> Read { get; set; }
    }

    public static class MachineState
    {
        private const int DefaultDumpCount = 16;
        private const int WordsPerDumpLine = 8;
        private const int ThumbnailColumns = 80;
        private const int ThumbnailRows = 30;
        private const string Shades = " .:-=+*#%@";

        public static void Print(Machine machine, IReadOnlyList<MemoryDump> dumps, TextWriter output)
        {
            foreach (var lcd in machine.Devices.OfType<CharacterDisplay>()) PrintDisplay(lcd, output);
            foreach (var screen in machine.Devices.OfType<IScreen>()) PrintScreen(screen, output);
            PrintWatches(machine, output);
            foreach (var dump in dumps) PrintDump(dump, output);
        }

        private static void PrintDisplay(CharacterDisplay lcd, TextWriter output)
        {
            output.WriteLine($"{lcd.ID()} ({lcd.DisplayName()}):");
            for (int row = 0; row < lcd.Rows; row++) output.WriteLine($"  |{lcd.Line(row)}|");
        }

        private static void PrintScreen(IScreen screen, TextWriter output)
        {
            var words = screen.Words;
            int lit = words.Count(w => w != 0);
            output.WriteLine($"{screen.ID()} ({screen.DisplayName()}): {lit:N0} of {words.Length:N0} pixels are not black ({screen.Kind}).");
            if (lit == 0 || words.Length != Framebuffer.Width * Framebuffer.Height) return;
            foreach (var line in Thumbnail(words, screen.IsDepth)) output.WriteLine($"  |{line}|");
        }

        private static IEnumerable<string> Thumbnail(ushort[] words, bool depth)
        {
            int blockWidth = Framebuffer.Width / ThumbnailColumns, blockHeight = Framebuffer.Height / ThumbnailRows;
            for (int row = 0; row < ThumbnailRows; row++)
            {
                var cells = Enumerable.Range(0, ThumbnailColumns).Select(column => Shade(words, column * blockWidth, row * blockHeight, blockWidth, blockHeight, depth));
                yield return new string(cells.ToArray());
            }
        }

        private static char Shade(ushort[] words, int left, int top, int width, int height, bool depth)
        {
            double total = 0;
            for (int y = top; y < top + height; y++)
                for (int x = left; x < left + width; x++) total += Brightness(words[y * Framebuffer.Width + x], depth);
            double average = total / (width * height);
            return Shades[(int)Math.Round(average * (Shades.Length - 1))];
        }

        private static double Brightness(ushort word, bool depth)
        {
            if (depth) return word / 65535.0;
            double red = (word >> 11) / 31.0, green = ((word >> 5) & 0x3F) / 63.0, blue = (word & 0x1F) / 31.0;
            return 0.299 * red + 0.587 * green + 0.114 * blue;
        }

        private static void PrintWatches(Machine machine, TextWriter output)
        {
            var values = new List<(string Key, int Value)>();
            foreach (var device in machine.Devices.OfType<IObservableState>()) device.Observe((key, read) => values.Add((key, read())));
            if (values.Count == 0) return;
            int width = values.Max(v => v.Key.Length);
            output.WriteLine("state:");
            foreach (var (key, value) in values) output.WriteLine($"  {key.PadRight(width)}  {value:X4}  {value}");
        }

        private static void PrintDump(MemoryDump dump, TextWriter output)
        {
            output.WriteLine($"{dump.Name} {dump.Start:X4}-{dump.Start + dump.Count - 1:X4}:");
            for (int address = dump.Start; address < dump.Start + dump.Count; address += WordsPerDumpLine)
            {
                var end = Math.Min(address + WordsPerDumpLine, dump.Start + dump.Count);
                var words = Enumerable.Range(address, end - address).Select(a => dump.Read(a).ToString("X4"));
                output.WriteLine($"  {address:X4}  {string.Join(" ", words)}");
            }
        }

        public static IReadOnlyList<MemoryDump> MemoryDumps(Machine machine, IEnumerable<string> specs)
        {
            return specs.Select(spec => Dump(machine, spec)).ToList();
        }

        private static MemoryDump Dump(Machine machine, string spec)
        {
            var parts = spec.Split(':');
            if (parts.Length < 2 || parts.Length > 3) throw new UsageException($"--memory takes <device>:<start>[:<count>], such as ram:0x100:16, not '{spec}'.");
            var (read, size) = Memory(machine, parts[0]);
            int start = Arguments.ParseNumber(parts[1], "The start in --memory");
            int count = parts.Length == 3 ? Arguments.ParseNumber(parts[2], "The count in --memory") : DefaultDumpCount;
            if (start + count > size) throw new UsageException($"{parts[0]} holds {size:N0} words, so {spec} reaches past its end.");
            return new MemoryDump { Name = parts[0], Start = start, Count = count, Read = read };
        }

        private static (Func<int, int> Read, int Size) Memory(Machine machine, string name)
        {
            var parts = name.Split('/');
            var device = machine.Device(parts[0]) ?? throw new UsageException($"There is no device '{parts[0]}'. The devices are {string.Join(", ", machine.Devices.Select(d => d.ID()))}.");
            return device switch
            {
                MMU mmu => Bank(mmu, parts),
                MemoryModule memory => (memory.ValueAt, memory.Size),
                IScreen screen => (address => screen.Words[address], screen.Words.Length),
                CharacterDisplay lcd => (lcd.ValueAt, lcd.Cells.Length),
                _ => throw new UsageException($"{parts[0]} has no memory to show."),
            };
        }

        private static (Func<int, int> Read, int Size) Bank(MMU mmu, string[] parts)
        {
            int bank = parts.Length > 1 ? Arguments.ParseNumber(parts[1], "The bank in --memory") : 0;
            if (bank >= mmu.RamBanks.Length) throw new UsageException($"{parts[0]} has banks 0 to {mmu.RamBanks.Length - 1}, not {bank}.");
            var memory = mmu.RamBanks[bank];
            return (memory.ValueAt, memory.Size);
        }
    }
}
