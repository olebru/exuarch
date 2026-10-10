using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class CommunityIndexTests
{
    private const string Index = """
        {
          // comments and trailing commas are fine
          "machines": [
            { "name": "ZED-8", "author": "someone", "tagline": "An eight bit machine", "tags": ["8 bit"], "minVersion": "1.38.0", "file": "machines/zed8.json", "image": "machines/zed8.png" },
            { "name": "ABC-16", "description": "Three registers. Nothing else.", "file": "abc.json" },
            { "name": "ABC-16", "file": "duplicate.json" },
            { "name": "Sneaky", "file": "../../secret.json" },
            { "name": "Absolute", "file": "https://example.com/x.json" },
            { "name": "", "file": "nameless.json" },
            { "name": "Bad image", "file": "ok.json", "image": "/etc/passwd" },
          ],
        }
        """;

    [Fact]
    public void TheIndexKeepsSafeNamedMachinesOnceEachInNameOrder()
    {
        var index = CommunityIndex.FromJson(Index);

        Assert.Equal(new[] { "ABC-16", "Bad image", "ZED-8" }, index.Machines.Select(m => m.Name));
        Assert.Equal("abc.json", index.Machines[0].File);
        Assert.Null(index.Machines[1].Image);
        Assert.Equal("machines/zed8.png", index.Machines[2].Image);
    }

    [Fact]
    public void SummariesAndSearchWorkLikeBuiltInPackages()
    {
        var index = CommunityIndex.FromJson(Index);

        Assert.Equal("Three registers", index.Machines[0].Summary);
        Assert.Equal("An eight bit machine", index.Machines[2].Summary);
        Assert.True(index.Machines[2].Matches("8 bit someone"));
        Assert.False(index.Machines[0].Matches("someone"));
    }

    [Theory]
    [InlineData("machines/a.json", true)]
    [InlineData("a.json", true)]
    [InlineData("../a.json", false)]
    [InlineData("machines/../a.json", false)]
    [InlineData("/a.json", false)]
    [InlineData("https://x/a.json", false)]
    [InlineData("a\\b.json", false)]
    [InlineData("a.json?x=1", false)]
    [InlineData("machines//a.json", false)]
    [InlineData("", false)]
    public void OnlyPathsInsideTheCollectionAreFollowed(string path, bool safe)
    {
        Assert.Equal(safe, CommunityIndex.IsSafeRelativePath(path));
    }

    [Fact]
    public void PathsResolveNextToTheIndex()
    {
        var url = CommunityIndex.Resolve(new Uri("https://raw.githubusercontent.com/olebru/exuarch-machines/main/index.json"), "machines/my cpu.json");

        Assert.Equal("https://raw.githubusercontent.com/olebru/exuarch-machines/main/machines/my%20cpu.json", url.AbsoluteUri);
        Assert.Throws<ArgumentException>(() => CommunityIndex.Resolve(new Uri("https://x/index.json"), "../up.json"));
    }

    [Theory]
    [InlineData("1.38.0", "1.38.0", true)]
    [InlineData("1.38.0", "1.39.2", true)]
    [InlineData("1.38.0", "1.37.9", false)]
    [InlineData("1.38.0", "1.38.0-pr.140", true)]
    [InlineData("1.38.0", "", true)]
    [InlineData(null, "1.0.0", true)]
    [InlineData("v2.0", "1.99.0", false)]
    public void AMachineSaysWhichVersionOfTheAppItNeeds(string needed, string app, bool runs)
    {
        Assert.Equal(runs, new CommunityMachine { MinVersion = needed }.RunsOn(app));
    }

    [Fact]
    public void AnIndexThatIsNotJsonOrTooLargeIsRefused()
    {
        Assert.Throws<MachineDefinitionException>(() => CommunityIndex.FromJson("not json"));
        Assert.Throws<MachineDefinitionException>(() => CommunityIndex.FromJson(new string(' ', CommunityIndex.MaxIndexLength + 1)));
        Assert.Empty(CommunityIndex.FromJson("{}").Machines);
    }
}
