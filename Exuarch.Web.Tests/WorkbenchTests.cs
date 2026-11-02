using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Exuarch.Web.Workbench;

namespace Exuarch.Web.Tests;

public class WorkbenchTests
{
    private sealed class MemoryStore : IBrowserStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public int Sets;
        public bool Reset;
        public bool PersistenceRequested;

        public Task<string> Get(string key) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task<bool> Set(string key, string value)
        {
            Sets++;
            Values[key] = value;
            return Task.FromResult(true);
        }
        public Task Keep(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
        public Task ResetAll()
        {
            Reset = true;
            return Task.CompletedTask;
        }
        public Task RequestPersistentStorage()
        {
            PersistenceRequested = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void TheDefaultMachineBuildsAndOneWithoutMicrocodeFailsThere()
    {
        var registry = DeviceRegistry.CreateDefault();
        var package = BuiltInPackages.Default;
        var built = ValidationResult.Of(package.Machine.Clone(), package.Programs[0].Source, new List<string>(), registry);
        Assert.Null(built.FailingStage);
        Assert.NotNull(built.Machine);
        var definition = package.Machine.Clone();
        definition.Decoder.Microcode = null;
        var failed = ValidationResult.Of(definition, "", new List<string>(), registry);
        Assert.Equal("microcode", failed.FailingStage);
        Assert.Null(failed.Machine);
        Assert.Equal("hardware", failed.WithParseErrors(new[] { "bad JSON" }).FailingStage);
    }

    [Fact]
    public void ASpeedNeedReadsAndWritesProgramNeeds()
    {
        Assert.Equal(new SpeedNeed(SpeedKind.Exact, 7), SpeedNeed.Of(new ProgramNeeds { ExactHz = 7 }, SpeedKind.Any));
        Assert.Equal(new SpeedNeed(SpeedKind.Min, 50), SpeedNeed.Of(new ProgramNeeds { MinHz = 50 }, SpeedKind.Any));
        Assert.Equal(new SpeedNeed(SpeedKind.Min, null), SpeedNeed.Of(null, SpeedKind.Min));
        var needs = new ProgramNeeds { ExactHz = 3 };
        new SpeedNeed(SpeedKind.Min, 10).ApplyTo(needs);
        Assert.Equal((10, (int?)null), (needs.MinHz.Value, needs.ExactHz));
        Assert.Equal(SpeedKind.Exact, SpeedNeed.Parse("exact"));
        Assert.Equal("any", new SpeedNeed(SpeedKind.Any, null).Value);
    }

    [Fact]
    public void ALinkGoesWhereItsKindIsRouted()
    {
        var followed = new List<string>();
        var links = new LinkRouter()
            .On("device", link => followed.Add($"device {link.Target}"))
            .On("guide", link => followed.Add($"{link.Kind} {link.Target}"));
        Assert.True(links.Follow(("device", "pc")));
        Assert.True(links.Follow(("guide", "microcode")));
        Assert.False(links.Follow(("nowhere", "x")));
        Assert.Equal(new[] { "device pc", "guide microcode" }, followed);
    }

    [Fact]
    public async Task ABurstOfChangesIsSavedOnceHalfASecondLater()
    {
        var store = new MemoryStore();
        var workspace = new WorkspaceController(store);
        workspace.MarkChanged();
        Assert.Equal(0, store.Sets);
        await workspace.Restore();
        workspace.MarkChanged();
        workspace.MarkChanged();
        workspace.SetNeedsKeypad(true);
        await Task.Delay(100);
        Assert.Equal(0, store.Sets);
        await Task.Delay(800);
        Assert.Equal(1, store.Sets);
        Assert.Contains("\"keypad\"", store.Values["exuarch.workspace"]);
    }

    private static async Task<WorkspaceController> EditedMinimalMachine(MemoryStore store)
    {
        var workspace = new WorkspaceController(store);
        await workspace.Restore();
        await workspace.CreateMachine("Mine", "minimal");
        workspace.ProgramChanged("; edited");
        workspace.KeepOpenPackage();
        return workspace;
    }

    private static Workspace Stored(MemoryStore store) => Workspace.FromJson(store.Values["exuarch.workspace"]);

    private sealed class MemoryRepo : IGitHubRepo
    {
        public string Connected;
        public string Failure;
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();

        public Task<string> Repo() => Task.FromResult(Connected);
        public Task<string> Connect(string repo, string token)
        {
            if (token != "good") return Task.FromResult("bad token");
            Connected = repo;
            return Task.FromResult<string>(null);
        }
        public Task Disconnect()
        {
            Connected = null;
            return Task.CompletedTask;
        }
        public Task<string> Write(string path, string content, string message)
        {
            if (Failure != null) return Task.FromResult(Failure);
            Files[path] = content;
            return Task.FromResult<string>(null);
        }
        public Task<GitHubFolder> List(string folder)
        {
            return Task.FromResult(new GitHubFolder { Paths = Files.Keys.Where(p => p.StartsWith(folder + "/")).ToList() });
        }
        public Task<GitHubFile> Read(string path)
        {
            return Task.FromResult(Files.TryGetValue(path, out var text) ? new GitHubFile { Text = text } : new GitHubFile { Missing = true });
        }
    }

    [Fact]
    public async Task ConnectingAddsTheRepositorysMachinesAndReplacesNothingThatDiffers()
    {
        var store = new MemoryStore();
        var repo = new MemoryRepo();
        var workspace = new WorkspaceController(store, null, repo);
        await workspace.Restore();
        await workspace.CreateMachine("Both", "minimal");
        var theirs = MachineTemplates.Minimal("Both");
        theirs.Description = "From the other computer";
        repo.Files["machines/Both.json"] = theirs.ToJson();
        repo.Files["machines/Theirs.json"] = MachineTemplates.Minimal("Theirs").ToJson();
        repo.Files["machines/Broken.json"] = "{ not a package";
        await workspace.CreateMachine("Mine", "minimal");

        Assert.Null(await workspace.ConnectGitHub("me/machines", "good"));

        Assert.Equal("Mine", workspace.PackageName);
        Assert.NotNull(Stored(store).LoadSaved("Theirs"));
        Assert.Equal("Mine", Stored(store).Open);
        Assert.True(repo.Files.ContainsKey("machines/Mine.json"));
        Assert.Equal(theirs.ToJson(), repo.Files["machines/Both.json"]);
        Assert.NotEqual("From the other computer", Stored(store).LoadSaved("Both")!.Value.Package.Description);
        Assert.Contains("1 machine added", workspace.GitHubNote);
        Assert.Contains("1 machine differs", workspace.GitHubNote);
        var states = workspace.LastRepoRead.Machines.ToDictionary(m => m.Name, m => m.State);
        Assert.Equal(RepoMachineState.Differs, states["Both"]);
        Assert.Equal(RepoMachineState.Unreadable, states["Broken"]);

        await workspace.UseGitHubVersion(workspace.LastRepoRead.Machines.First(m => m.Name == "Both").Package);
        Assert.Equal("From the other computer", Stored(store).LoadSaved("Both")!.Value.Package.Description);
        Assert.Equal("Mine", Stored(store).Open);
        var read = await workspace.ReadGitHubMachines();
        Assert.Equal(RepoMachineState.Same, read.Machines.First(m => m.Name == "Both").State);
    }

    [Fact]
    public async Task KeepingTheBrowsersVersionWritesItToTheRepository()
    {
        var repo = new MemoryRepo();
        var workspace = new WorkspaceController(new MemoryStore(), null, repo);
        await workspace.Restore();
        await workspace.CreateMachine("Both", "minimal");
        var theirs = MachineTemplates.Minimal("Both");
        theirs.Description = "From the other computer";
        repo.Files["machines/Both.json"] = theirs.ToJson();
        await workspace.ConnectGitHub("me/machines", "good");

        Assert.Null(await workspace.KeepBrowserVersion("Both"));
        Assert.DoesNotContain("From the other computer", repo.Files["machines/Both.json"]);
    }

    [Fact]
    public async Task AMachineWithUnsavedChangesIsNotReplacedOnConnecting()
    {
        var store = new MemoryStore();
        var repo = new MemoryRepo();
        var workspace = new WorkspaceController(store, null, repo);
        await workspace.Restore();
        workspace.ProgramChanged("; unsaved");
        workspace.KeepOpenPackage();
        var edited = BuiltInPackages.Get(workspace.PackageName);
        edited.Description = "Edited on the other computer";
        repo.Files[GitHubRepo.MachinePath(edited.Name)] = edited.ToJson();

        await workspace.ConnectGitHub("me/machines", "good");

        Assert.Equal("; unsaved", workspace.Program);
        Assert.True(workspace.HasUnsavedChanges);
        Assert.Equal(RepoMachineState.Differs, workspace.LastRepoRead.Machines.Single().State);
    }

    [Fact]
    public async Task ConnectingCopiesTheSavedMachinesAndEverySaveWritesToTheRepo()
    {
        var store = new MemoryStore();
        var repo = new MemoryRepo();
        var workspace = new WorkspaceController(store, null, repo);
        await workspace.Restore();
        await workspace.CreateMachine("Mine", "minimal");
        workspace.ProgramChanged("; edited");
        workspace.KeepOpenPackage();

        Assert.Equal("bad token", await workspace.ConnectGitHub("me/machines", "wrong"));
        Assert.Null(workspace.ConnectedRepo);
        Assert.Null(await workspace.ConnectGitHub("me/machines", "good"));
        Assert.Equal("me/machines", workspace.ConnectedRepo);
        Assert.Equal(MachineTemplates.StarterProgram, MachinePackage.FromJson(repo.Files["machines/Mine.json"]).Programs[0].Source);

        await workspace.Save();
        Assert.Equal("; edited", MachinePackage.FromJson(repo.Files["machines/Mine.json"]).Programs[0].Source);

        repo.Failure = "offline";
        await workspace.SaveAs("Other");
        Assert.Contains("offline", workspace.StorageWarning);
        Assert.NotNull(Stored(store).LoadSaved("Other"));

        await workspace.DisconnectGitHub();
        Assert.Null(workspace.ConnectedRepo);
    }

    [Fact]
    public void AMachineNameBecomesASafeFileName()
    {
        Assert.Equal("machines/BYOC-16.json", GitHubRepo.MachinePath("BYOC-16"));
        Assert.Equal("machines/a-b-c.json", GitHubRepo.MachinePath("a/b:c"));
        Assert.Equal("machines/unnamed.json", GitHubRepo.MachinePath("  "));
    }

    [Fact]
    public async Task AnEditIsKeptAsADraftUntilSaved()
    {
        var store = new MemoryStore();
        var workspace = await EditedMinimalMachine(store);
        await Task.Delay(800);
        Assert.True(workspace.HasUnsavedChanges);
        Assert.Equal(MachineTemplates.StarterProgram, Stored(store).LoadSaved("Mine")!.Value.Package.Programs[0].Source);
        Assert.Equal("; edited", Stored(store).Load("Mine")!.Value.Package.Programs[0].Source);

        await workspace.Save();
        Assert.False(workspace.HasUnsavedChanges);
        Assert.Equal("; edited", Stored(store).LoadSaved("Mine")!.Value.Package.Programs[0].Source);
        Assert.True(store.PersistenceRequested);
    }

    [Fact]
    public async Task DiscardingChangesLoadsTheSavedVersion()
    {
        var store = new MemoryStore();
        var workspace = await EditedMinimalMachine(store);
        await workspace.DiscardChanges();
        Assert.False(workspace.HasUnsavedChanges);
        Assert.Equal(MachineTemplates.StarterProgram, workspace.Program);
        Assert.False(Stored(store).HasUnsavedChanges("Mine"));
    }

    [Fact]
    public async Task SaveAsSavesACopyAndLeavesTheOriginalAsSaved()
    {
        var store = new MemoryStore();
        var workspace = await EditedMinimalMachine(store);
        await workspace.SaveAs("Mine");
        Assert.Equal("Mine 2", workspace.PackageName);
        Assert.Equal("Mine 2", workspace.Definition.Name);
        Assert.False(workspace.HasUnsavedChanges);
        var stored = Stored(store);
        Assert.Equal("; edited", stored.LoadSaved("Mine 2")!.Value.Package.Programs[0].Source);
        Assert.Equal(MachineTemplates.StarterProgram, stored.LoadSaved("Mine")!.Value.Package.Programs[0].Source);
        Assert.False(stored.HasUnsavedChanges("Mine"));
    }

    [Fact]
    public async Task SaveAsOfAnEditedExampleProgramKeepsTheEditInTheCopy()
    {
        var store = new MemoryStore();
        var workspace = new WorkspaceController(store);
        await workspace.Restore();
        var example = workspace.CurrentProgram.Name;
        workspace.ProgramChanged("; mine");
        await workspace.SaveAs("Copy");
        Assert.Equal("; mine", Stored(store).LoadSaved("Copy")!.Value.Package.Programs.Find(p => p.Name == example).Source);
        Assert.Null(Stored(store).Find(BuiltInPackages.Default.Name));
    }

    [Fact]
    public async Task UnsavedChangesFromTheLastVisitAreRecoveredWithANote()
    {
        var store = new MemoryStore();
        await EditedMinimalMachine(store);
        await Task.Delay(800);
        var next = new WorkspaceController(store);
        await next.Restore();
        Assert.Equal("Mine", next.PackageName);
        Assert.Equal("; edited", next.Program);
        Assert.True(next.HasUnsavedChanges);
        Assert.NotNull(next.RecoveredNote);
        await next.Save();
        Assert.Null(next.RecoveredNote);
    }

    [Fact]
    public async Task AnUnreadableSaveIsPutAside()
    {
        var store = new MemoryStore();
        store.Values["exuarch.workspace"] = "{ not json";
        var workspace = new WorkspaceController(store);
        await workspace.Restore();
        Assert.Equal("{ not json", store.Values["exuarch.workspace.unreadable"]);
        Assert.NotNull(workspace.StorageWarning);
    }

    [Fact]
    public void ANewProgramIsAddedToThePackageUnderAFreeName()
    {
        var workspace = new WorkspaceController(new MemoryStore());
        int count = workspace.Package.Programs.Count;
        workspace.NewProgramName = workspace.Package.Programs[0].Name;
        workspace.CreateProgram();
        Assert.Equal(count + 1, workspace.Package.Programs.Count);
        Assert.Equal($"{workspace.Package.Programs[0].Name} 2", workspace.CurrentProgram.Name);
        Assert.Null(workspace.NewProgramName);
        Assert.True(workspace.IsYours(workspace.CurrentProgram));
    }

    [Fact]
    public void AnExactSpeedTheSliderCanNotBeSetToIsMovedAndShowsANote()
    {
        var workspace = new WorkspaceController(new MemoryStore());
        workspace.SetSpeedKind(SpeedKind.Exact);
        Assert.Equal(new SpeedNeed(SpeedKind.Exact, null), workspace.Speed);
        workspace.SetSpeedHz("123457");
        Assert.Equal(SpeedKind.Exact, workspace.Speed.Kind);
        Assert.Equal(ClockSlider.Nearest(123457), workspace.Speed.Hz);
        Assert.NotNull(workspace.NeedsNote);
    }
}
