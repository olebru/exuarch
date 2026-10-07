using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Exuarch.Web.Tests;

// Golden files of the page's markup, which has to stay exactly the same when the components under it are rebuilt. A
// test compares what it renders now with the file in Snapshots, kept gzipped since markup repeats a lot. Set
// EXUARCH_ACCEPT_SNAPSHOTS=1 to write the files instead, after a deliberate change.
internal static class Snapshot
{
    private static string Folder([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

    public static bool Accepting => Environment.GetEnvironmentVariable("EXUARCH_ACCEPT_SNAPSHOTS") == "1";

    public static void Match(string name, string actual)
    {
        var file = Path.Combine(Folder(), "Snapshots", Safe(name) + ".txt.gz");
        var received = Path.Combine(Folder(), "Snapshots", Safe(name) + ".received.txt");
        actual = actual.Replace("\r\n", "\n");
        if (Accepting)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            Write(file, actual);
            if (File.Exists(received)) File.Delete(received);
            return;
        }
        Assert.True(File.Exists(file), $"No snapshot {file}. Run the tests with EXUARCH_ACCEPT_SNAPSHOTS=1 to write it.");
        var expected = Read(file);
        if (expected == actual)
        {
            if (File.Exists(received)) File.Delete(received);
            return;
        }
        File.WriteAllText(received, actual);
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        int line = 0;
        while (line < e.Length && line < a.Length && e[line] == a[line]) line++;
        Assert.Fail($"{name} differs from its snapshot at line {line + 1}:\n  expected: {(line < e.Length ? e[line] : "<end>")}\n  actual:   {(line < a.Length ? a[line] : "<end>")}\nThe whole output is in {received}.");
    }

    private static void Write(string file, string text)
    {
        using var stream = File.Create(file);
        using var zip = new GZipStream(stream, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(zip, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Read(string file)
    {
        using var stream = File.OpenRead(file);
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Long output is kept as a hash, with the first lines written out so a difference can be found.
    public sealed class Digest : IDisposable
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly StringBuilder head = new StringBuilder();
        private readonly int headLines;
        public int Lines { get; private set; }

        public Digest(int headLines) { this.headLines = headLines; }

        public void Add(string line)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(line + "\n"));
            if (Lines++ < headLines) head.Append("  ").Append(line).Append('\n');
        }

        public string Finish(string title)
        {
            return $"{title}: {Lines} lines, sha256 {Convert.ToHexString(hash.GetHashAndReset())}\n{head}";
        }

        public void Dispose() { hash.Dispose(); }
    }

    private static string Safe(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name) builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' ? c : '_');
        return builder.ToString();
    }
}
