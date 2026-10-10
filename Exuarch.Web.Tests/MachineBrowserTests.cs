using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Exuarch.Web.Drawer;

namespace Exuarch.Web.Tests;

public class MachineBrowserTests
{
    private static readonly MachinePackage Mine = new MachinePackage { Name = "My CPU", Description = "Two registers and a loop. Nothing else.", Machine = new MachineDefinition() };

    private static IReadOnlyList<MachineGroup> Groups(MachineBrowser browser, MachinePackage open = null, params MachinePackage[] own)
    {
        return browser.Groups(own, open ?? BuiltInPackages.Default);
    }

    private static string[] Names(IReadOnlyList<MachineGroup> groups) => groups.SelectMany(g => g.Machines).Select(m => m.Name).ToArray();

    [Fact]
    public void AllShowsYourMachinesThenTheExamplesByLevelAndNoTutorialStarts()
    {
        var groups = Groups(new MachineBrowser(), null, Mine);

        Assert.Equal(new[] { "Yours", "Simple", "Advanced", "Ludicrous" }, groups.Select(g => g.Title));
        Assert.Equal(BuiltInPackages.Examples.Count() + 1, Names(groups).Length);
        Assert.DoesNotContain(Names(groups), n => n.StartsWith("Tutorial"));
    }

    [Fact]
    public void YourGroupIsLeftOutWhenYouHaveNone()
    {
        Assert.Equal(new[] { "Simple", "Advanced", "Ludicrous" }, Groups(new MachineBrowser()).Select(g => g.Title));
    }

    [Fact]
    public void ALevelChipShowsThatLevelOnly()
    {
        var browser = new MachineBrowser();
        browser.Choose("Simple");

        Assert.Equal(new[] { "TINY-16", "BYOC-16", "STACK-16" }, Names(Groups(browser, null, Mine)));
    }

    [Fact]
    public void YoursShowsOnlyYourOwn()
    {
        var browser = new MachineBrowser();
        browser.Choose(MachineBrowser.Yours);

        Assert.Equal(new[] { "My CPU" }, Names(Groups(browser, null, Mine)));
        Assert.Empty(Groups(browser));
    }

    [Fact]
    public void GraphicsShowsTheMachinesThatDraw()
    {
        var browser = new MachineBrowser();
        browser.Choose(MachineBrowser.Graphics);
        var names = Names(Groups(browser, null, Mine));

        Assert.Contains("GPU-16", names);
        Assert.Contains("BLAZE-16", names);
        Assert.DoesNotContain("TINY-16", names);
        Assert.DoesNotContain("My CPU", names);
    }

    [Fact]
    public void SearchMatchesEveryWordInNamesTaglinesDescriptionsAndTags()
    {
        var browser = new MachineBrowser { Query = "3d" };
        Assert.Equal(new[] { "FLIP-16", "GPU-16", "TURBO-16" }, Names(Groups(browser)));

        browser.Query = "stack machine";
        Assert.Contains("STACK-16", Names(Groups(browser)));
        Assert.DoesNotContain("GPU-16", Names(Groups(browser)));

        browser.Query = "loop";
        Assert.Contains("My CPU", Names(Groups(browser, null, Mine)));
    }

    [Fact]
    public void AnOpenTutorialStartIsShownAtTheTop()
    {
        var tutorial = BuiltInPackages.Get(TutorialMachines.Starts[0].PackageName);
        var groups = Groups(new MachineBrowser(), tutorial);

        Assert.Equal(MachineBrowser.Open, groups[0].Title);
        Assert.Equal(tutorial.Name, groups[0].Machines.Single().Name);
    }

    [Fact]
    public void TheCommunityChipShowsOnlyCommunityMachinesThatMatch()
    {
        var browser = new MachineBrowser();
        var machines = new[] { new CommunityMachine { Name = "ZED-8", Tagline = "Eight bits" }, new CommunityMachine { Name = "ABC-16", Tagline = "Three registers" } };
        Assert.Empty(browser.CommunityShown(machines));

        browser.Choose(MachineBrowser.Community);
        Assert.Empty(browser.Groups(new MachinePackage[0], BuiltInPackages.Default));
        Assert.Equal(2, browser.CommunityShown(machines).Count);

        browser.Query = "eight";
        Assert.Equal("ZED-8", Assert.Single(browser.CommunityShown(machines)).Name);
    }

    [Fact]
    public void GalleryPreviewsReadImagesAndLcdText()
    {
        var previews = GalleryPreviews.Parse("""{ "GPU-16": { "image": "gpu16.png" }, "TINY-16": { "lcd": ["Hi", ""] } }""", "https://x/machines/");

        Assert.Equal("https://x/machines/gpu16.png", previews["GPU-16"].Image);
        Assert.Equal(new[] { "Hi", "" }, previews["TINY-16"].Lcd);
        Assert.Null(previews["TINY-16"].Image);
    }

    [Fact]
    public void ClickingAMachineOpensItsDetailsAndClickingAgainClosesThem()
    {
        var browser = new MachineBrowser();
        browser.Toggle("RISC-16");
        Assert.Equal("RISC-16", browser.Expanded);

        browser.Toggle("DSP-16");
        Assert.Equal("DSP-16", browser.Expanded);

        browser.Toggle("DSP-16");
        Assert.Null(browser.Expanded);
    }
}
