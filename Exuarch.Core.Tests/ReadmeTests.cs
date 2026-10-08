using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ReadmeTests
{
    [Fact]
    public void EveryExampleHasAReadmeThatIntroducesIt()
    {
        foreach (var package in BuiltInPackages.Examples)
        {
            Assert.False(string.IsNullOrWhiteSpace(package.Readme), $"{package.Name} has no README");
            Assert.StartsWith($"# {package.Name}", package.Readme);
            Assert.True(package.Readme.Length > 1000, $"{package.Name}'s README is only {package.Readme.Length} characters");
            Assert.Contains("## Things to try", package.Readme);
        }
    }

    [Fact]
    public void EveryReadmeLinkLeadsSomewhere()
    {
        int links = 0;
        foreach (var package in BuiltInPackages.All)
        {
            foreach (var href in ReadmeLinks.In(package.Readme))
            {
                links++;
                var problem = ReadmeLinks.Problem(package, href);
                Assert.True(problem == null, $"{package.Name}: {href}: {problem}");
            }
        }
        Assert.True(links > 50, $"only {links} links");
    }

    [Theory]
    [InlineData("exuarch:device/alu", "device", "alu")]
    [InlineData("exuarch:program/Hello,%20world%20on%20the%20LCD", "program", "Hello, world on the LCD")]
    [InlineData("exuarch:tab/Hardware design", "tab", "Hardware design")]
    public void LinksParse(string href, string kind, string target)
    {
        Assert.True(ReadmeLinks.TryParse(href, out var k, out var t));
        Assert.Equal((kind, target), (k, t));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("exuarch:nothing/here")]
    [InlineData("exuarch:device/")]
    [InlineData("exuarch:device")]
    public void OtherLinksDoNotParse(string href)
    {
        Assert.False(ReadmeLinks.TryParse(href, out _, out _));
    }

    [Fact]
    public void BrokenLinksAreExplained()
    {
        var package = BuiltInPackages.Get("BYOC-16");
        Assert.Contains("no device 'nope'", ReadmeLinks.Problem(package, "exuarch:device/nope"));
        Assert.Contains("no instruction 'NOPE'", ReadmeLinks.Problem(package, "exuarch:instruction/NOPE"));
        Assert.Contains("no program 'Nope'", ReadmeLinks.Problem(package, "exuarch:program/Nope"));
        Assert.Contains("no tab 'Nope'", ReadmeLinks.Problem(package, "exuarch:tab/Nope"));
        Assert.Contains("no built in package 'NOPE-16'", ReadmeLinks.Problem(package, "exuarch:package/NOPE-16"));
        Assert.Null(ReadmeLinks.Problem(package, "exuarch:package/RISC-16"));
    }

    [Fact]
    public void TheReadmeTravelsWithAPackageFile()
    {
        var package = BuiltInPackages.Get("MOVE-16");
        var back = MachinePackage.FromJson(package.ToJson());
        Assert.Equal(package.Readme, back.Readme);
        // Package files from before READMEs still open.
        var old = MachinePackage.FromJson("{ \"name\": \"Old\", \"machine\": " + package.Machine.ToJson() + " }");
        Assert.Null(old.Readme);
    }

    [Fact]
    public void NewMachinesStartWithAReadmeToFillIn()
    {
        Assert.StartsWith("# Mine", MachineTemplates.Minimal("Mine").Readme);
        Assert.Contains("minimal CPU", MachineTemplates.Minimal("Mine").Readme);
        Assert.StartsWith("# Blank", MachineTemplates.Empty("Blank").Readme);
        var copy = MachineTemplates.CopyOf(BuiltInPackages.Get("RISC-16"), "My RISC");
        Assert.Equal(BuiltInPackages.Get("RISC-16").Readme, copy.Readme);
    }
}
