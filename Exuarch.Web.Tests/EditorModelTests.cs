using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Exuarch.Web.Hardware;
using Exuarch.Web.Microcoding;
using Microsoft.AspNetCore.Components.Web;

namespace Exuarch.Web.Tests;

// The editors' plain C# parts, without a page: keyboard chords, what is selected, the gestures on the design canvas and
// dragging in the microcode editor.
public class EditorModelTests
{
    private static KeyboardEventArgs Key(string key, bool ctrl = false, bool shift = false, bool meta = false)
    {
        return new KeyboardEventArgs { Key = key, CtrlKey = ctrl, ShiftKey = shift, MetaKey = meta };
    }

    [Fact]
    public void AChordNeedsTheModifiersItNamesAndIgnoresTheOthers()
    {
        var undo = KeyChord.Parse("Mod+Z");
        Assert.True(undo.Matches(Key("z", ctrl: true)));
        Assert.True(undo.Matches(Key("Z", meta: true, shift: true)));
        Assert.False(undo.Matches(Key("z")));
        var delete = KeyChord.Parse("Delete");
        Assert.True(delete.Matches(Key("Delete", ctrl: true)));
        Assert.False(delete.Matches(Key("delete")));
    }

    [Fact]
    public async Task TheFirstMatchingChordRuns()
    {
        var ran = new List<string>();
        var keys = new KeyMap()
            .On("Mod+Shift+Z", () => ran.Add("redo"))
            .On("Mod+Z", () => ran.Add("undo"))
            .On("Mod+Y", () => ran.Add("redo"))
            .On("Escape", () => ran.Add("escape"));
        await keys.Dispatch(Key("z", ctrl: true));
        await keys.Dispatch(Key("Z", ctrl: true, shift: true));
        await keys.Dispatch(Key("y", meta: true, shift: true));
        await keys.Dispatch(Key("Escape", ctrl: true));
        await keys.Dispatch(Key("q", ctrl: true));
        Assert.Equal(new[] { "undo", "redo", "redo", "escape" }, ran);
    }

    private static MachineDefinition Tiny() => BuiltInPackages.Default.Machine.Clone();

    [Fact]
    public void ASelectionOfSomethingGoneIsDropped()
    {
        var definition = Tiny();
        var device = definition.Devices[0].Id;
        Assert.Equal(new DeviceSelection(device), new DeviceSelection(device).Revalidate(definition));
        Assert.Equal(Selection.None, new DeviceSelection("nothing").Revalidate(definition));
        Assert.Equal(Selection.None, new BusSelection("nothing").Revalidate(definition));
        Assert.Equal(Selection.Decoder, Selection.Decoder.Revalidate(null));
    }

    [Fact]
    public void AProblemSelectsTheDeviceItNamesOrTheDecoder()
    {
        var definition = Tiny();
        var device = definition.Devices[1].Id;
        Assert.Equal(new DeviceSelection(device), Selection.ForProblem(definition, $"Device '{device}' (x): needs a bus"));
        Assert.Equal(Selection.Decoder, Selection.ForProblem(definition, "\"decoder.status\" names no device"));
        Assert.Null(Selection.ForProblem(definition, "something else"));
    }

    // A design with its own undo history, counting what it hands to the page.
    private static (MachineDesign Design, EditHistory History, List<MachineDefinition> Changes) Design()
    {
        var changes = new List<MachineDefinition>();
        MachineDesign design = null;
        var history = new EditHistory(() => design.Definition.ToJson(), json =>
        {
            design.Update(MachineDefinition.FromJson(json), design.Registry, design.History, design.Errors, null);
            return Task.CompletedTask;
        });
        design = new MachineDesign(definition =>
        {
            changes.Add(definition);
            return Task.CompletedTask;
        });
        design.Update(Tiny(), DeviceRegistry.CreateDefault(), history, new List<string>(), null);
        return (design, history, changes);
    }

    private static readonly ElementRect Canvas = new ElementRect { Left = 100, Top = 50, Width = 1200, Height = 800 };

    [Fact]
    public async Task MovingACardSnapsItToTheGridAndIsOneStepInTheHistory()
    {
        var (design, history, changes) = Design();
        var device = design.Definition.Devices[0];
        var (x, y) = (device.Layout.X, device.Layout.Y);
        var interaction = new CanvasInteraction();
        var gesture = new MoveDeviceGesture(design, device);
        Assert.Equal(new DeviceSelection(device.Id), gesture.Picks);
        interaction.Begin(gesture, 200, 200, design.Definition.ToJson());
        interaction.Place(gesture, Canvas, 200, 200);
        interaction.Move(233, 208);
        Assert.True(interaction.IsLayoutDraft);
        Assert.Equal((x + 30, y + 10), (device.Layout.X, device.Layout.Y));
        await interaction.Release(233, 208, design.Layout);
        Assert.False(interaction.Dragging);
        Assert.Single(changes);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public async Task LeavingTheEditorPutsAMovedCardBack()
    {
        var (design, history, _) = Design();
        var device = design.Definition.Devices[0];
        var (x, y) = (device.Layout.X, device.Layout.Y);
        var interaction = new CanvasInteraction();
        var gesture = new MoveDeviceGesture(design, device);
        interaction.Begin(gesture, 200, 200, design.Definition.ToJson());
        interaction.Place(gesture, Canvas, 200, 200);
        interaction.Move(300, 300);
        await interaction.Leave();
        var restored = design.Definition.FindDevice(device.Id);
        Assert.Equal((x, y), (restored.Layout.X, restored.Layout.Y));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public async Task ATypeClickedInThePaletteIsAddedOnTheFirstBusAndSelected()
    {
        var (design, history, _) = Design();
        int count = design.Definition.Devices.Count;
        var interaction = new CanvasInteraction();
        var gesture = new NewDeviceGesture(design, "register");
        interaction.Begin(gesture, 10, 10, design.Definition.ToJson());
        interaction.Place(gesture, Canvas, 10, 10);
        await interaction.Release(11, 11, design.Layout);
        Assert.Equal(count + 1, design.Definition.Devices.Count);
        var added = design.Definition.Devices.Last();
        Assert.Equal(new DeviceSelection(added.Id), design.Selection);
        Assert.Equal(design.Definition.Buses[0].Id, added.Ports().First().Value);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void APortDraggedNearABusLightsItUp()
    {
        var (design, _, _) = Design();
        var device = design.Definition.Devices[0];
        var bus = design.Definition.Buses[0];
        var gesture = new WirePortGesture(design, device, design.Info(device).Ports[0]);
        var interaction = new CanvasInteraction();
        interaction.Begin(gesture, 0, 0, design.Definition.ToJson());
        interaction.Place(gesture, new ElementRect(), 0, 0);
        interaction.Move(500, bus.Layout.Y + 5);
        Assert.True(interaction.Highlights(bus));
        Assert.NotNull(interaction.Sketch);
    }

    [Fact]
    public async Task ARefusedRenameSaysWhyAndLeavesNoHistory()
    {
        var (design, history, _) = Design();
        var first = design.Definition.Devices[0].Id;
        design.SelectDevice(design.Definition.Devices[1].Id);
        await design.RenameSelectedDevice(first);
        Assert.NotNull(design.RenameError);
        Assert.False(history.CanUndo);
        await design.RenameSelectedDevice("acc");
        Assert.Null(design.RenameError);
        Assert.Equal(new DeviceSelection("acc"), design.Selection);
    }

    // A session on the default machine's microcode, with the fetch routine picked.
    private static MicrocodeSession Session()
    {
        var machine = Tiny();
        MicrocodeSession session = null;
        session = new MicrocodeSession(microcode => Task.CompletedTask, device => Task.CompletedTask);
        var history = new EditHistory(() => session.Microcode.ToJson(), json => Task.CompletedTask);
        session.Update(machine.Decoder.Microcode, machine, new List<MicrocodeDiagnostic>(), DeviceRegistry.CreateDefault(), history, FocusRequest.None);
        return session;
    }

    [Fact]
    public async Task AnInstructionDroppedOnAnotherTakesItsPlace()
    {
        var session = Session();
        var instructions = session.Microcode.Instructions;
        var (first, second) = (instructions[0], instructions[1]);
        session.Drag(new InstructionDrag(0));
        await session.DropOnStep(0);
        session.Drag(new InstructionDrag(0));
        await session.DropOnInstruction(1);
        Assert.Equal(new[] { second, first }, instructions.Take(2));
    }

    [Fact]
    public async Task ASignalFromThePaletteDroppedOnAddStepGoesIntoANewStep()
    {
        var session = Session();
        session.Select(session.Microcode.Instructions[0]);
        int steps = session.Selected.Steps.Count;
        session.Drag(new PaletteSignalDrag("a.load"));
        await session.DropOnNewStep();
        Assert.Equal(steps + 1, session.Selected.Steps.Count);
        Assert.Equal(new[] { "a.load" }, session.Selected.Steps.Last().Signals);
        Assert.Equal(steps, session.ActiveStep);
        await session.DropOnNewStep();
        Assert.Equal(steps + 1, session.Selected.Steps.Count);
    }

    [Fact]
    public async Task AMnemonicMustBeOneWordNoOtherInstructionHas()
    {
        var session = Session();
        session.Select(session.Microcode.Instructions[0]);
        await session.Rename("two words");
        Assert.Equal("Mnemonic must be a single word.", session.RenameError);
        var other = session.Microcode.Instructions[1].Mnemonic;
        await session.Rename(other.ToLowerInvariant());
        Assert.Equal($"'{other}' is already defined.", session.RenameError);
        await session.Rename("zz");
        Assert.Null(session.RenameError);
        Assert.Equal("ZZ", session.Selected.Mnemonic);
    }

    [Fact]
    public void BusActivityListsWhoDrivesAndWhoReadsEachBus()
    {
        var machine = Tiny();
        var catalog = new SignalCatalog(machine, DeviceRegistry.CreateDefault());
        var step = machine.Decoder.Microcode.Fetch.Steps.First(s => s.Signals.Count > 1);
        var activity = catalog.BusActivity(step);
        Assert.NotEmpty(activity);
        Assert.All(activity, transfer => Assert.True(transfer.Drivers.Count + transfer.Readers.Count > 0));
        Assert.Equal("floating", new BusFlow("main", new List<string>(), new List<string> { "a" }).State);
        Assert.Equal("conflict", new BusFlow("main", new List<string> { "a", "b" }, new List<string>()).State);
    }
}
