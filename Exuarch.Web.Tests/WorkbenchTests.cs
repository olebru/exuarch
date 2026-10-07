using System.Collections.Generic;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Exuarch.Web.Workbench;

namespace Exuarch.Web.Tests;

// The page's plain C# parts: the workspace and keeping it, checking it stage by stage, what a program needs of the
// clock and where links go.
public class WorkbenchTests
{
    // Storage in memory, which writes down what was kept.
    private sealed class MemoryStore : IBrowserStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public int Sets;
        public bool Reset;

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
    public void AnExactSpeedTheSliderCanNotBeSetToIsMovedAndSaysSo()
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
