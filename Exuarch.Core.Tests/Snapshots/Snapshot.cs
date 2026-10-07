using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Exuarch.Core.Tests.Snapshots;

// Golden files for behaviour that has to stay exactly the same when the code under it is rebuilt: what the assembler,
// the validators, the simulator and the layout make of every built in package. A test compares what it makes now
// with the file in this folder. Set EXUARCH_ACCEPT_SNAPSHOTS=1 to write the files instead, after a deliberate change.
internal static class Snapshot
{
    private static string Folder([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

    public static bool Accepting => Environment.GetEnvironmentVariable("EXUARCH_ACCEPT_SNAPSHOTS") == "1";

    public static void Match(string name, string actual)
    {
        var file = Path.Combine(Folder(), "Approved", Safe(name) + ".txt");
        var received = Path.ChangeExtension(file, ".received.txt");
        actual = actual.Replace("\r\n", "\n");
        if (Accepting)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, actual);
            if (File.Exists(received)) File.Delete(received);
            return;
        }
        Assert.True(File.Exists(file), $"No snapshot {file}. Run the tests with EXUARCH_ACCEPT_SNAPSHOTS=1 to write it.");
        var expected = File.ReadAllText(file).Replace("\r\n", "\n");
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
