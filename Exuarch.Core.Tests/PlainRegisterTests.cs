using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Exuarch.Core.Tests;

public class PlainRegisterTests
{
    private static readonly HashSet<string> PeopleVerbs = new()
    {
        "refuse", "refuses", "refused", "answers", "answered", "knows", "knew", "wants", "wanted",
        "remembers", "remembered", "forgets", "forgot", "forgotten", "owes", "owed", "owns", "agrees", "disagrees",
        "promises", "tells", "told", "decides", "decided", "sits", "lands", "landed", "offers", "obeys", "swallows",
        "complains", "believes", "understands",
    };

    private static readonly HashSet<string> TestNameOnlyVerbs = new() { "says", "said", "asks", "asked" };

    private static readonly HashSet<string> NameVerbs = new() { "names", "named", "naming" };
    private static readonly HashSet<string> WordsAfterANameVerb = new() { "the", "a", "an", "its", "which", "what", "that", "it", "every" };
    private static readonly HashSet<string> WordsBeforeANameVerb = new() { "it", "that", "which" };

    private static readonly Regex PersonSubject = new(@"\b(you|your|yourself|we|reader|anyone|someone|people|programmer)\b", RegexOptions.IgnoreCase);
    private static readonly Regex TestMethod = new(@"public (?:async )?(?:void|Task) (\w+)\(");
    private static readonly Regex CamelWord = new(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+");
    private static readonly Regex ProseWord = new(@"[A-Za-z]+");
    private static readonly Regex Sentence = new(@"(?<=[.!?:])\s+");

    private static string Root([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));

    private static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root(), folder), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string Relative(string file) => Path.GetRelativePath(Root(), file);

    private static IEnumerable<string> DeniedWords(IReadOnlyList<string> words, HashSet<string> denied)
    {
        for (int i = 0; i < words.Count; i++)
        {
            if (denied.Contains(words[i])) yield return words[i];
            else if (NameVerbs.Contains(words[i]) && IsNameVerb(words, i)) yield return words[i];
        }
    }

    private static bool IsNameVerb(IReadOnlyList<string> words, int i) =>
        (i + 1 < words.Count && WordsAfterANameVerb.Contains(words[i + 1])) || (i > 0 && WordsBeforeANameVerb.Contains(words[i - 1]));

    private static List<string> Words(Regex word, string text) => word.Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();

    [Fact]
    public void NoTestNameUsesAPeopleVerb()
    {
        var denied = PeopleVerbs.Concat(TestNameOnlyVerbs).ToHashSet();
        var testFiles = new[] { "Exuarch.Core.Tests", "Exuarch.Web.Tests", "Exuarch.Cli.Tests" }.SelectMany(p => Files(p, "*.cs"));
        var findings = new List<string>();
        foreach (var file in testFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var name = TestMethod.Match(lines[i]);
                if (!name.Success) continue;
                foreach (var verb in DeniedWords(Words(CamelWord, name.Groups[1].Value), denied))
                    findings.Add($"{Relative(file)}:{i + 1} {name.Groups[1].Value} uses \"{verb}\"");
            }
        }
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void NoGuideOrExampleSentenceAboutAMachineUsesAPeopleVerb()
    {
        var texts = Files("Exuarch.Core/Guides", "*.md")
            .Concat(Files("Exuarch.Core/Packages", "*.md"))
            .Concat(Files("Exuarch.Core/Packages", "*.asm"))
            .Concat(Files("Exuarch.Core/Packages", "*.json"));
        var findings = new List<string>();
        foreach (var file in texts)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var sentence in Sentence.Split(lines[i]).Where(s => !PersonSubject.IsMatch(s)))
                {
                    foreach (var verb in DeniedWords(Words(ProseWord, sentence), PeopleVerbs))
                        findings.Add($"{Relative(file)}:{i + 1} \"{verb}\" in: {sentence.Trim()}");
                }
            }
        }
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }
}
