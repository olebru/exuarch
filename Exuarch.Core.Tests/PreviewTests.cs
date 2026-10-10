using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class PreviewTests
{
    private const int MaxTicks = 12_000_000;
    private const int Scale = 2;

    private static string Folder([CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "..", "Exuarch.Web", "wwwroot", "machines");

    private static bool Writing => Environment.GetEnvironmentVariable("EXUARCH_WRITE_PREVIEWS") == "1";

    public static string Slug(string name) => new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    [Fact]
    public void EveryExampleHasAPreviewOfWhatItsFirstProgramShows()
    {
        if (Writing) Write();
        var previews = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), "previews.json"))).RootElement;
        foreach (var package in BuiltInPackages.Examples)
        {
            Assert.True(previews.TryGetProperty(package.Name, out var preview), $"{package.Name} has no preview: run the tests with EXUARCH_WRITE_PREVIEWS=1");
            if (preview.TryGetProperty("image", out var image)) Assert.True(File.Exists(Path.Combine(Folder(), image.GetString()!)), $"{package.Name}'s preview image is missing");
            else Assert.True(preview.TryGetProperty("lcd", out _), $"{package.Name}'s preview shows nothing");
        }
    }

    private static void Write()
    {
        Directory.CreateDirectory(Folder());
        var previews = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var package in BuiltInPackages.Examples)
        {
            var program = package.Programs.FirstOrDefault(p => p.Name == package.Preview) ?? package.Programs.First();
            var machine = new Machine(package.Machine, program.Source) { RecordHistory = false };
            foreach (var _ in machine.Run().Take(MaxTicks)) { }
            var screen = machine.Devices.OfType<Framebuffer>().FirstOrDefault();
            var lcd = machine.Devices.OfType<CharacterDisplay>().FirstOrDefault();
            if (screen != null && screen.Shown.Any(p => p != 0))
            {
                var file = Slug(package.Name) + ".png";
                File.WriteAllBytes(Path.Combine(Folder(), file), Png(screen.Shown));
                previews[package.Name] = new Dictionary<string, object> { ["image"] = file, ["program"] = program.Name };
            }
            else if (lcd != null)
            {
                previews[package.Name] = new Dictionary<string, object> { ["lcd"] = LcdLines(lcd), ["program"] = program.Name };
            }
        }
        File.WriteAllText(Path.Combine(Folder(), "previews.json"), JsonSerializer.Serialize(previews, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static List<string> LcdLines(CharacterDisplay lcd)
    {
        return Enumerable.Range(0, lcd.Rows)
            .Select(row => new string(lcd.Cells.Skip(row * lcd.Columns).Take(lcd.Columns).Select(c => c >= 32 && c < 127 ? (char)c : ' ').ToArray()).TrimEnd())
            .ToList();
    }

    private static byte[] Png(ushort[] pixels)
    {
        int width = Framebuffer.Width / Scale, height = Framebuffer.Height / Scale;
        var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = Framebuffer.ToRgb(Brightest(pixels, x * Scale, y * Scale));
                raw.WriteByte((byte)r);
                raw.WriteByte((byte)g);
                raw.WriteByte((byte)b);
            }
        }
        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) raw.WriteTo(zlib);
        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 2;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static ushort Brightest(ushort[] pixels, int left, int top)
    {
        ushort best = 0;
        int bestLight = -1;
        for (int dy = 0; dy < Scale; dy++)
        {
            for (int dx = 0; dx < Scale; dx++)
            {
                var pixel = pixels[(top + dy) * Framebuffer.Width + left + dx];
                var (r, g, b) = Framebuffer.ToRgb(pixel);
                int light = r + g + b;
                if (light > bestLight) { best = pixel; bestLight = light; }
            }
        }
        return best;
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        png.Write(length);
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(typed));
        png.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }
}
