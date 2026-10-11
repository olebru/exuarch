using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class WorkspaceTests
{
    private static MachinePackage Opened(string name)
    {
        var package = BuiltInPackages.Get(name);
        package.Machine.EnsureLayout();
        return package;
    }

    [Fact]
    public void ABuiltInPackageIsOnlyKeptWhileItHasChanges()
    {
        var workspace = new Workspace();
        var byoc = Opened("BYOC-16");
        workspace.Save(byoc, byoc.Programs[0].Name, byoc.Programs[0].Source);
        Assert.Empty(workspace.Packages);
        Assert.False(workspace.IsEdited("BYOC-16"));
        Assert.Equal("BYOC-16", workspace.Open);

        byoc.Machine.Devices.First(d => d.Id == "rega").Name = "ACCUMULATOR";
        workspace.Save(byoc, byoc.Programs[0].Name, byoc.Programs[0].Source);
        Assert.True(workspace.IsEdited("BYOC-16"));

        byoc.Machine.Devices.First(d => d.Id == "rega").Name = BuiltInPackages.Get("BYOC-16").Machine.Devices.First(d => d.Id == "rega").Name;
        workspace.Save(byoc, byoc.Programs[0].Name, byoc.Programs[0].Source);
        Assert.False(workspace.IsEdited("BYOC-16"));
    }

    [Fact]
    public void MovingPartsIsKeptButIsNotAnEdit()
    {
        var workspace = new Workspace();
        var byoc = Opened("BYOC-16");
        byoc.Machine.Devices.First(d => d.Id == "rega").Layout.X += 120;
        byoc.Machine.Buses[0].Layout.Y += 40;
        byoc.Machine.Decoder.Layout.X += 60;
        workspace.Save(byoc, byoc.Programs[0].Name, byoc.Programs[0].Source);

        Assert.False(workspace.IsEdited("BYOC-16"));
        var kept = workspace.Load("BYOC-16")!.Value.Package.Machine;
        Assert.Equal(byoc.Machine.Devices.First(d => d.Id == "rega").Layout.X, kept.Devices.First(d => d.Id == "rega").Layout.X);
        Assert.Equal(byoc.Machine.Buses[0].Layout.Y, kept.Buses[0].Layout.Y);

        byoc.Machine.Devices.First(d => d.Id == "rega").Name = "ACCUMULATOR";
        workspace.Save(byoc, byoc.Programs[0].Name, byoc.Programs[0].Source);
        Assert.True(workspace.IsEdited("BYOC-16"));
    }

    [Fact]
    public void AnEditedExampleProgramIsAChange()
    {
        var workspace = new Workspace();
        var risc = Opened("RISC-16");
        var program = risc.Programs[1];
        workspace.Save(risc, program.Name, program.Source);
        Assert.False(workspace.IsEdited("RISC-16"));
        workspace.Save(risc, program.Name, program.Source + "\n; mine");
        Assert.True(workspace.IsEdited("RISC-16"));
        var (package, name, source) = workspace.Load("RISC-16").Value;
        Assert.Equal(program.Name, name);
        Assert.EndsWith("; mine", source);
        Assert.Equal(program.Source, package.Program(program.Name).Source);
    }

    [Fact]
    public void EverythingSurvivesTheBrowser()
    {
        var workspace = new Workspace();
        var mine = MachineTemplates.Minimal("My machine");
        mine.Programs.Add(new PackageProgram { Name = "Starter program", Source = MachineTemplates.StarterProgram });
        mine.Readme = "# My machine\n\nNotes.";
        workspace.Save(mine, "Starter program", MachineTemplates.StarterProgram);
        var harvard = Opened("HARVARD-16");
        harvard.Readme += "\n\nMy notes.";
        workspace.Save(harvard, harvard.Programs[0].Name, harvard.Programs[0].Source);

        var back = Workspace.FromJson(workspace.ToJson());
        Assert.Equal("HARVARD-16", back.Open);
        Assert.Equal(new[] { "My machine" }, back.OwnPackages.Select(p => p.Name));
        Assert.True(back.IsEdited("HARVARD-16"));
        Assert.EndsWith("My notes.", back.Load("HARVARD-16").Value.Package.Readme);
        var (own, programName, _) = back.Load("My machine").Value;
        Assert.Equal("Starter program", programName);
        Assert.Equal(mine.ToJson(), own.ToJson());
        Assert.Empty(Machine.ValidateDefinition(own.Machine, DeviceRegistry.CreateDefault()));
    }

    [Fact]
    public void ResetRemovesTheChanges()
    {
        var workspace = new Workspace();
        var move = Opened("MOVE-16");
        move.Machine.Name = "Moved";
        workspace.Save(move, null, null);
        Assert.True(workspace.IsEdited("MOVE-16"));
        workspace.Remove("MOVE-16");
        Assert.False(workspace.IsEdited("MOVE-16"));
        Assert.Equal(BuiltInPackages.Get("MOVE-16").ToJson(), workspace.Load("MOVE-16").Value.Package.ToJson());
    }

    [Fact]
    public void LoadingGivesACopy()
    {
        var workspace = new Workspace();
        var mine = MachineTemplates.Empty("Blank");
        workspace.Save(mine, null, null);
        var loaded = workspace.Load("Blank").Value.Package;
        loaded.Machine.Buses.Clear();
        Assert.NotEmpty(workspace.Load("Blank").Value.Package.Machine.Buses);
        Assert.Null(workspace.Load("Nothing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"packages\": [ { \"package\": { \"name\": \"X\" } } ] }")]
    [InlineData("{ \"version\": 1, \"surprise\": true }")]
    public void ADamagedSaveGivesAnEmptyWorkspace(string json)
    {
        var workspace = Workspace.FromJson(json);
        Assert.Empty(workspace.Packages);
    }

    [Fact]
    public void AnUnreadableSaveIsReportedSoItCanBeKeptAside()
    {
        Assert.True(Workspace.TryFromJson(null, out _));
        Assert.True(Workspace.TryFromJson(new Workspace().ToJson(), out _));
        Assert.False(Workspace.TryFromJson("not json", out var empty));
        Assert.Empty(empty.Packages);
    }

    [Fact]
    public void NamesStayUnique()
    {
        var workspace = new Workspace();
        Assert.Equal("My machine", workspace.UniqueName("My machine"));
        workspace.Save(MachineTemplates.Empty("My machine"), null, null);
        Assert.Equal("My machine 2", workspace.UniqueName("My machine"));
        Assert.Equal("BYOC-16 2", workspace.UniqueName("BYOC-16"));
    }

    [Fact]
    public void AllBuiltInPackagesTogetherAreSmallEnoughToKeep()
    {
        var workspace = new Workspace();
        foreach (var package in BuiltInPackages.All)
        {
            var edited = Opened(package.Name);
            edited.Readme += " (edited)";
            workspace.Save(edited, null, null);
        }
        Assert.Equal(BuiltInPackages.All.Count, workspace.Packages.Count);
        Assert.True(workspace.ToJson().Length < 2_000_000, $"{workspace.ToJson().Length} characters");
    }
}
