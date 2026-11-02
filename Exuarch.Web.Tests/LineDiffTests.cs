using System.Linq;
using Exuarch.Web.Components;

namespace Exuarch.Web.Tests;

public class LineDiffTests
{
    [Fact]
    public void CompareListsRemovedLinesBeforeAddedOnesBetweenUnchangedLines()
    {
        var lines = LineDiff.Compare("a\nb\nc\nd", "a\nB\nc\nd\ne");
        Assert.Equal(new[] { "  a", "- b", "+ B", "  c", "  d", "+ e" }, lines.Select(Show));
        Assert.Equal((2, 0), (lines[1].BeforeNumber, lines[1].AfterNumber));
        Assert.Equal((0, 5), (lines[5].BeforeNumber, lines[5].AfterNumber));
    }

    [Fact]
    public void CompareTreatsCrLfAndATrailingNewlineAsUnchanged()
    {
        Assert.All(LineDiff.Compare("a\r\nb\r\n", "a\nb"), l => Assert.Equal(DiffKind.Same, l.Kind));
        Assert.Equal(new[] { "+ a" }, LineDiff.Compare(null, "a").Select(Show));
    }

    [Fact]
    public void WithContextFoldsUnchangedRunsAwayFromTheChanges()
    {
        var before = string.Join("\n", Enumerable.Range(1, 20));
        var after = before.Replace("\n10\n", "\nten\n");
        var shown = LineDiff.WithContext(LineDiff.Compare(before, after), 2);
        Assert.Equal(new[] { "… 7", "  8", "  9", "- 10", "+ ten", "  11", "  12", "… 8" }, shown.Select(Show));
    }

    private static string Show(DiffLine line) => line.Kind switch
    {
        DiffKind.Removed => $"- {line.Text}",
        DiffKind.Added => $"+ {line.Text}",
        DiffKind.Skipped => $"… {line.SkippedCount}",
        _ => $"  {line.Text}",
    };
}
