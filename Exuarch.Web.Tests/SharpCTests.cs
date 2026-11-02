using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Core.SharpC;
using Exuarch.Web.SharpC;

namespace Exuarch.Web.Tests;

public class SharpCTests
{
    private static async Task<SharpCWorkspace> Restored(MemoryStore store)
    {
        var workspace = new SharpCWorkspace(store);
        await workspace.Restore();
        return workspace;
    }

    [Fact]
    public async Task AnEditedExampleIsADraftAndSaveAsKeepsItAsAProgramOfYourOwn()
    {
        var store = new MemoryStore();
        var workspace = await Restored(store);
        Assert.Equal(Sharp16.Examples[0].CSharp, workspace.Text);
        workspace.Edit(workspace.Text + "\n// mine");
        Assert.True(workspace.IsDirty);
        Assert.Null(workspace.SaveOpen());

        var program = workspace.SaveAs("First");
        Assert.Equal("mine:First", workspace.Open);
        Assert.False(workspace.IsDirty);
        Assert.False(workspace.Drafts.ContainsKey(SharpCWorkspace.ExampleKey(0)));
        Assert.EndsWith("// mine", program.Text);
        Assert.Equal("First 2", workspace.SaveAs("First").Name);

        await workspace.Persist();
        var again = await Restored(store);
        Assert.Equal("mine:First 2", again.Open);
        Assert.Equal(new[] { "First", "First 2" }, again.Saved.Select(p => p.Name));
    }

    [Fact]
    public async Task UnsavedChangesComeBackWithANoteAndDiscardDropsThem()
    {
        var store = new MemoryStore();
        var workspace = await Restored(store);
        workspace.SaveAs("Mine");
        workspace.Edit("// changed");
        await workspace.Persist();

        var again = await Restored(store);
        Assert.Equal("// changed", again.Text);
        Assert.NotNull(again.RecoveredNote);
        again.Discard();
        Assert.False(again.IsDirty);
        Assert.Equal(Sharp16.Examples[0].CSharp, again.Text);
        Assert.Null(again.RecoveredNote);
    }

    [Fact]
    public async Task TheStorageFormatOfTheStaticPageStillLoads()
    {
        var store = new MemoryStore();
        store.Values[SharpCWorkspace.ProgramsKey] = "[{\"name\":\"Old\",\"text\":\"// old\"}]";
        store.Values[SharpCWorkspace.DraftsKey] = "{\"mine:Old\":\"// newer\",\"mine:Gone\":\"x\"}";
        store.Values[SharpCWorkspace.OpenKey] = "\"mine:Old\"";
        var workspace = await Restored(store);
        Assert.Equal("// newer", workspace.Text);
        Assert.Single(workspace.Drafts);

        var legacy = new MemoryStore();
        legacy.Values[SharpCWorkspace.LegacyDraftKey] = "{\"text\":\"// from long ago\",\"example\":\"1\"}";
        var migrated = await Restored(legacy);
        Assert.Equal(SharpCWorkspace.ExampleKey(1), migrated.Open);
        Assert.Equal("// from long ago", migrated.Text);
        Assert.Equal("null", legacy.Values[SharpCWorkspace.LegacyDraftKey]);
    }

    [Fact]
    public async Task DeletingOpensTheFirstExample()
    {
        var workspace = await Restored(new MemoryStore());
        workspace.SaveAs("Mine");
        workspace.DeleteOpen();
        Assert.Empty(workspace.Saved);
        Assert.Equal(SharpCWorkspace.ExampleKey(0), workspace.Open);
    }

    [Fact]
    public async Task TheDownloadedPackageHasYourProgramsAndTheExamplesAndValidates()
    {
        var workspace = await Restored(new MemoryStore());
        workspace.SaveAs("Mine");
        workspace.OpenProgram(SharpCWorkspace.ExampleKey(1));
        workspace.Edit(workspace.Text + "\n// edited");
        var package = workspace.FullPackage(true);
        var names = package.Programs.Select(p => p.Name).ToList();
        Assert.Equal("Mine", names[0]);
        Assert.Equal($"My {Sharp16.Examples[1].Name}", names[1]);
        Assert.Equal(Sharp16.Examples.Select(e => e.Name), names.Skip(2));
        Assert.Equal(package.ToJson(), MachinePackage.FromJson(package.ToJson()).ToJson());
        Assert.DoesNotContain(names, n => n.StartsWith("My ") && n != $"My {Sharp16.Examples[1].Name}");
        Assert.Equal(names.Count - 1, workspace.FullPackage(false).Programs.Count);
    }

    [Fact]
    public async Task SavingWritesTheProgramAndKeepsSharp16InTheRepositoryUpToDate()
    {
        var repo = new MemoryRepo();
        var gitHub = new SharpCGitHub(repo);
        var workspace = await Restored(new MemoryStore());
        var first = workspace.SaveAs("Blink/er");
        Assert.Null(await gitHub.Connect("me/box", "good", workspace.Saved));
        Assert.Equal(first.Text, repo.Files["sharpc/Blink-er.cs"]);
        Assert.False(repo.Files.ContainsKey(SharpCGitHub.MachinePath));
        Assert.Equal(SharpMachineState.Missing, await gitHub.MachineState());

        Assert.Null(await gitHub.AddMachine(workspace.FullPackage(false)));
        workspace.Edit(first.Text + "\n// second");
        await gitHub.Save(workspace.SaveOpen());
        var machine = MachinePackage.FromJson(repo.Files[SharpCGitHub.MachinePath]);
        Assert.Single(machine.Programs, p => p.Name == "Blink/er");
        Assert.Contains("// second", machine.Programs.Single(p => p.Name == "Blink/er").Source);

        workspace.Edit("this does not compile");
        await gitHub.Save(workspace.SaveOpen());
        Assert.Equal("this does not compile", repo.Files["sharpc/Blink-er.cs"]);
        Assert.Contains("does not compile", gitHub.Failure);
        Assert.Contains("// second", MachinePackage.FromJson(repo.Files[SharpCGitHub.MachinePath]).Programs.Single(p => p.Name == "Blink/er").Source);
    }
}
