using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class HandbookTests
{
    private static readonly string[] TutorialIds = { "ground-zero", "fetch-routine", "opcodes-are-addresses", "ending-an-instruction", "first-machine", "registers-and-the-alu", "loops-and-flags", "subroutines-and-the-stack", "reading-the-keypad", "taking-an-interrupt", "stack-machine" };
    private static readonly string[] ConceptIds =
    {
        "buses-and-ticks", "devices-and-control-lines", "microcode", "fetch-and-the-instruction-register", "flags-and-conditions", "operands",
        "assembly", "memory-and-banks", "bridges", "bus-masters", "interrupts", "packages", "graphics-pipeline", "real-hardware", "safety",
    };

    [Fact]
    public void TheHandbookStartsWithGettingStartedThenTheTutorialsInOrderThenTheConcepts()
    {
        Assert.Equal("getting-started", Guides.All[0].Id);
        Assert.Equal("Getting started", Guides.All[0].Title);
        Assert.Equal(TutorialIds, Guides.All.Where(g => g.Section == "Tutorials").Select(g => g.Id));
        Assert.Equal(ConceptIds, Guides.All.Where(g => g.Section == "Concepts").Select(g => g.Id));
        Assert.Equal(Guides.All.Count, Guides.All.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void EveryPageHasATitleAndEndsWithWhereToGoNext()
    {
        foreach (var guide in Guides.All)
        {
            Assert.StartsWith($"# {guide.Title}", guide.Markdown);
            Assert.True(guide.Markdown.Length > 1500, $"{guide.Id} is only {guide.Markdown.Length} characters");
        }
        foreach (var guide in Guides.All.Where(g => g.Section == "Concepts")) Assert.Contains("## See also", guide.Markdown);
        foreach (var guide in Guides.All.Where(g => g.Section == "Tutorials")) Assert.Contains("## Next", guide.Markdown);
    }

    [Fact]
    public void EveryLinkInTheHandbookLeadsSomewhere()
    {
        var pages = Guides.All.Select(g => (g.Id, g.Markdown))
            .Concat(DeviceReference.Types.Select(t => ($"reference/{t.Type}", DeviceReference.Markdown(t.Type))));
        foreach (var (id, markdown) in pages)
        {
            foreach (var href in ReadmeLinks.In(markdown))
            {
                var problem = ReadmeLinks.Problem(null, href);
                Assert.True(problem == null, $"{id}: {href}: {problem}");
            }
        }
    }

    [Fact]
    public void TheChangelogIsAStartPageThatSearchLeavesOut()
    {
        var changelog = Guides.Find(Guides.ChangelogId);
        Assert.Equal("Changelog", changelog.Title);
        Assert.Equal("Start", changelog.Section);
        Assert.Equal(changelog, Guides.All[1]);
        Assert.Contains("## [1.16.0]", changelog.Markdown);
        Assert.DoesNotContain(Handbook.Search("release"), hit => hit.Target == Guides.ChangelogId);
    }

    [Fact]
    public void GettingStartedLeadsToEveryTutorialAndConcept()
    {
        var start = Guides.Find("getting-started").Markdown;
        foreach (var id in TutorialIds.Concat(ConceptIds)) Assert.Contains($"exuarch:guide/{id})", start);
        foreach (var package in BuiltInPackages.Examples) Assert.Contains($"exuarch:package/{package.Name})", start);
    }

    // The contents list every device with a few words on what it is: the start of its description.
    [Fact]
    public void EveryDeviceTypeHasAShortSummaryForTheContents()
    {
        foreach (var type in DeviceReference.Types)
        {
            var summary = DeviceReference.Summary(type);
            Assert.False(string.IsNullOrWhiteSpace(summary), type.Type);
            Assert.StartsWith(summary, type.Description);
            Assert.True(summary.Length <= 100, $"{type.Type}: {summary}");
        }
        Assert.Equal("Holds one 16 bit value between ticks", DeviceReference.Summary(DeviceReference.Types.First(t => t.Type == "register")));
    }

    [Fact]
    public void EveryDeviceTypeHasAReferencePageWithAllItsLines()
    {
        var registry = DeviceRegistry.CreateDefault();
        Assert.Equal(registry.Types, DeviceReference.Types.Select(t => t.Type));
        foreach (var info in registry.TypeInfos)
        {
            var page = DeviceReference.Markdown(info.Type);
            Assert.StartsWith($"# {info.Type}\n", page);
            foreach (var line in info.ControlLines) Assert.Contains($"| `{line.Name}` |", page);
            foreach (var parameter in info.Parameters) Assert.Contains($"| `{parameter.Name}` |", page);
            foreach (var connection in info.Connections) Assert.Contains($"- `{connection.Name}`:", page);
        }
        // Where the examples use it.
        Assert.Contains("[COPRO-16](exuarch:package/COPRO-16)", DeviceReference.Markdown("blitter"));
        Assert.False(DeviceReference.Exists("eeprom"));
    }

    [Theory]
    [InlineData("exuarch:guide/microcode", "guide", "microcode")]
    [InlineData("exuarch:reference/dualPortRegister", "reference", "dualPortRegister")]
    public void GuideAndReferenceLinksParse(string href, string kind, string target)
    {
        Assert.True(ReadmeLinks.TryParse(href, out var k, out var t));
        Assert.Equal((kind, target), (k, t));
        Assert.Null(ReadmeLinks.Problem(null, href));
        Assert.Null(ReadmeLinks.Problem(BuiltInPackages.Get("BYOC-16"), href));
    }

    [Fact]
    public void BrokenHandbookLinksAreExplained()
    {
        Assert.Contains("no guide 'nope'", ReadmeLinks.Problem(null, "exuarch:guide/nope"));
        Assert.Contains("no device type 'eeprom'", ReadmeLinks.Problem(null, "exuarch:reference/eeprom"));
        Assert.Contains("only to tabs, packages, guides and the reference", ReadmeLinks.Problem(null, "exuarch:device/alu"));
    }

    [Fact]
    public void SearchFindsPagesByTitleFirst()
    {
        var hits = Handbook.Search("microcode");
        Assert.Equal(("guide", "microcode"), (hits[0].Kind, hits[0].Target));
        Assert.Contains(hits, h => h.Kind == "reference");

        var alu = Handbook.Search("ALU");
        Assert.Contains(alu, h => h.Kind == "reference" && h.Target == "alu");
        Assert.All(alu, h => Assert.False(string.IsNullOrWhiteSpace(h.Snippet)));

        Assert.Empty(Handbook.Search(""));
        Assert.Empty(Handbook.Search("   "));
        Assert.Empty(Handbook.Search("zzqx"));
    }

    [Fact]
    public void EveryWordOfTheQueryHasToMatch()
    {
        var both = Handbook.Search("interrupt timer");
        Assert.NotEmpty(both);
        Assert.All(both, h => Assert.True(Handbook.Search("interrupt").Any(o => o.Target == h.Target) && Handbook.Search("timer").Any(o => o.Target == h.Target)));
    }

    [Fact]
    public void SearchIncludesTheOpenMachine()
    {
        var package = BuiltInPackages.Get("BYOC-16");
        var lai = Handbook.Search("LAI", package);
        Assert.Contains(lai, h => h.Kind == "instruction" && h.Target == "LAI" && h.Section == Handbook.MachineSection);
        Assert.Contains(Handbook.Search("mmu", package), h => h.Kind == "device" && h.Target == "mmu");
        Assert.DoesNotContain(Handbook.Search("LAI"), h => h.Kind == "instruction");
    }
}
