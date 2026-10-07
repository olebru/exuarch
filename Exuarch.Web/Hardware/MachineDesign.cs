using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Hardware
{
    // The machine on the design canvas, what is wrong with it and what is selected in it. Every edit the canvas, the
    // palette and the inspectors make goes through here: it is recorded in the undo history shared with the microcode
    // editor and handed to the page, which validates the machine and builds it again.
    public sealed class MachineDesign
    {
        private const double Grid = 10;
        private const double BusSnapDistance = 22;

        private readonly Func<MachineDefinition, Task> changed;
        private SchematicLayout layout;
        private MachineDefinition layoutFor;
        private DeviceRegistry layoutRegistry;

        public MachineDesign(Func<MachineDefinition, Task> changed)
        {
            this.changed = changed;
        }

        // Something only the editor shows changed: the selection, a rename that was refused or a delete to confirm.
        public event Action StateChanged;

        public MachineDefinition Definition { get; private set; }
        public DeviceRegistry Registry { get; private set; }
        public EditHistory History { get; private set; }
        public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();
        // The machine as built, when it builds, for the state the cards show.
        public Machine Preview { get; private set; }
        public Selection Selection { get; private set; } = Selection.None;
        public string RenameError { get; private set; }
        // A device the microcode uses, waiting for the choice of what to do with its signals.
        public PendingDelete PendingDelete { get; private set; }

        // The editor's parameters, every time the page renders it. A definition it has not seen before (another
        // machine, an undo, an edit of the JSON) is laid out, and a selection of something it no longer has is dropped.
        public void Update(MachineDefinition definition, DeviceRegistry registry, EditHistory history, IReadOnlyList<string> errors, Machine preview)
        {
            Registry = registry;
            History = history;
            Errors = errors;
            Preview = preview;
            if (ReferenceEquals(Definition, definition)) return;
            Definition = definition;
            definition?.EnsureLayout();
            Selection = Selection.Revalidate(definition);
        }

        // ---- Geometry ----

        // The drawing's geometry, the same as the run view uses.
        public SchematicLayout Layout
        {
            get
            {
                if (!ReferenceEquals(layoutFor, Definition) || !ReferenceEquals(layoutRegistry, Registry))
                {
                    layoutFor = Definition;
                    layoutRegistry = Registry;
                    layout = new SchematicLayout(Definition, Registry);
                }
                return layout;
            }
        }

        public DeviceTypeInfo Info(DeviceDefinition device) => Layout.Info(device);

        public string BusColor(string busId) => Palette.Bus(Definition, busId);

        public static double Snap(double value) => Math.Round(value / Grid) * Grid;

        public DeviceDefinition DeviceAt(double x, double y)
        {
            return Definition.Devices.LastOrDefault(d => d.Layout != null
                && x >= d.Layout.X && x <= d.Layout.X + SchematicLayout.CardWidth
                && y >= d.Layout.Y && y <= d.Layout.Y + Layout.CardHeight(d));
        }
        public BusDefinition BusNear(double y)
        {
            return Definition.Buses.Where(b => b.Layout != null && Math.Abs(b.Layout.Y - y) <= BusSnapDistance)
                                   .OrderBy(b => Math.Abs(b.Layout.Y - y)).FirstOrDefault();
        }
        private BusDefinition NearestBus(double y)
        {
            return Definition.Buses.Where(b => b.Layout != null).OrderBy(b => Math.Abs(b.Layout.Y - y)).FirstOrDefault();
        }

        // ---- Problems and state ----

        public IEnumerable<string> ProblemsOf(string id) => Errors.Where(e => e.Contains($"'{id}'"));
        public bool HasProblem(string id) => ProblemsOf(id).Any();
        // Problems with the decoder name it: "decoder" or "decoder.status" and so on.
        public bool DecoderHasProblem => Errors.Any(e => e.Contains("\"decoder"));

        // The micro step the decoder is at, read from its instruction register when the machine builds.
        public int? DecoderStep
        {
            get
            {
                var id = Definition.Decoder?.InstructionRegister;
                return id != null && Preview?.Device(id) is InstructionRegister register ? register.Data : null;
            }
        }

        // Where the microcode uses a device's signals: the instructions, with their steps and lines.
        public List<(string Mnemonic, List<(int Step, string Line)> Steps)> UsageSummary(string deviceId)
        {
            return Definition.SignalUsages(deviceId)
                .GroupBy(u => u.Instruction.Mnemonic)
                .Select(g => (g.Key, g.Select(u => (u.Step, u.Signal.Substring(deviceId.Length + 1))).ToList()))
                .ToList();
        }

        // ---- Selection ----

        public void Select(Selection selection)
        {
            Selection = selection;
            RenameError = null;
            StateChanged?.Invoke();
        }
        public void SelectDevice(string id) => Select(new DeviceSelection(id));
        public void SelectBus(string id) => Select(new BusSelection(id));
        public void SelectDecoder() => Select(Selection.Decoder);
        public void ClearSelection() => Select(Selection.None);

        // ---- Editing ----

        private async Task Mutate(Action change)
        {
            History?.Record();
            change();
            await changed(Definition);
        }
        // Keeps a change already made, such as a card moved by dragging, with the snapshot taken before it.
        public async Task Commit(string snapshot)
        {
            History?.Push(snapshot);
            await changed(Definition);
        }
        // Puts another definition in place of this one, without recording it: a drag that is given up.
        public async Task Replace(MachineDefinition definition)
        {
            Definition = definition;
            Selection = Selection.Revalidate(definition);
            await changed(definition);
        }
        public Task Undo() => History?.Undo() ?? Task.CompletedTask;
        public Task Redo() => History?.Redo() ?? Task.CompletedTask;
        public bool CanUndo => History?.CanUndo == true;
        public bool CanRedo => History?.CanRedo == true;

        private static string IdPrefix(string type)
        {
            return type switch
            {
                "register" => "reg",
                "statusRegister" => "status",
                "instructionRegister" => "ir",
                "dualPortRegister" => "bridge",
                "clock" => "clk",
                _ => type.ToLowerInvariant(),
            };
        }

        // A device dropped on the canvas at (x, y), on the bus nearest to it.
        public Task AddDevice(string type, double x, double y)
        {
            var device = new DeviceDefinition { Id = Definition.NextFreeId(IdPrefix(type)), Type = type };
            device.Layout = new Position { X = Snap(x), Y = Snap(y) };
            ConnectFirstPort(device, NearestBus(device.Layout.Y + SchematicLayout.CardMinHeight / 2));
            return Add(device);
        }
        // A device clicked in the palette: on the first bus, in the first free spot beside it.
        public Task AddDevice(string type)
        {
            var device = new DeviceDefinition { Id = Definition.NextFreeId(IdPrefix(type)), Type = type };
            ConnectFirstPort(device, Definition.Buses.FirstOrDefault(b => b.Layout != null));
            device.Layout = SchematicPlacement.FreeSpot(Definition, device);
            return Add(device);
        }
        private void ConnectFirstPort(DeviceDefinition device, BusDefinition bus)
        {
            var ports = Registry.Info(device.Type).Ports;
            if (bus != null && ports.Count > 0) device.SetPortBus(ports[0], bus.Id);
        }
        private async Task Add(DeviceDefinition device)
        {
            await Mutate(() => Definition.Devices.Add(device));
            SelectDevice(device.Id);
        }

        public async Task AddBus()
        {
            var id = Definition.NextFreeId("bus");
            var y = Definition.Buses.Select(b => b.Layout?.Y ?? 0).DefaultIfEmpty(SchematicLayout.FirstBusY - SchematicLayout.BusSpacing).Max() + SchematicLayout.BusSpacing;
            await Mutate(() => Definition.Buses.Add(new BusDefinition { Id = id, Layout = new Position { Y = Snap(y) } }));
            SelectBus(id);
        }

        public async Task AddDecoder()
        {
            if (Definition.Decoder?.Layout != null) { SelectDecoder(); return; }
            await Mutate(() =>
            {
                Definition.Decoder ??= new DecoderDefinition();
                Definition.EnsureLayout();
            });
            SelectDecoder();
        }

        public Task DeleteSelected()
        {
            return Selection switch
            {
                DeviceSelection device => DeleteDevice(device.Id),
                BusSelection bus => DeleteBus(bus.Id),
                _ => Task.CompletedTask,
            };
        }
        // A device the microcode uses is only deleted after asking what to do with its signals.
        private async Task DeleteDevice(string id)
        {
            var usages = Definition.SignalUsages(id);
            if (usages.Count > 0)
            {
                PendingDelete = new PendingDelete(id, usages);
                StateChanged?.Invoke();
                return;
            }
            Selection = Selection.None;
            await Mutate(() => Definition.RemoveDevice(id));
        }
        private async Task DeleteBus(string id)
        {
            Selection = Selection.None;
            await Mutate(() => Definition.RemoveBus(id));
        }
        public async Task ConfirmDelete(bool removeSignals)
        {
            if (PendingDelete == null) return;
            var id = PendingDelete.Id;
            PendingDelete = null;
            Selection = Selection.None;
            await Mutate(() =>
            {
                Definition.RemoveDevice(id);
                if (removeSignals) Definition.RemoveSignalsOf(id);
            });
        }
        public void CancelDelete()
        {
            PendingDelete = null;
            StateChanged?.Invoke();
        }

        public async Task DuplicateSelected()
        {
            if (Selection is not DeviceSelection selected || Definition.FindDevice(selected.Id) is not { } source) return;
            var copy = Definition.Clone().FindDevice(source.Id);
            copy.Id = Definition.NextFreeId(IdPrefix(source.Type));
            copy.Name = null;
            copy.Layout = new Position { X = source.Layout.X + 30, Y = source.Layout.Y + 30 };
            await Mutate(() => Definition.Devices.Add(copy));
            SelectDevice(copy.Id);
        }

        public Task AutoLayout() => Mutate(() => Definition.EnsureLayout(force: true));

        public Task RenameSelectedDevice(string newId)
        {
            var oldId = (Selection as DeviceSelection)?.Id;
            return Rename(newId, id => Definition.RenameDevice(oldId, id), id => new DeviceSelection(id));
        }
        public Task RenameSelectedBus(string newId)
        {
            var oldId = (Selection as BusSelection)?.Id;
            return Rename(newId, id => Definition.RenameBus(oldId, id), id => new BusSelection(id));
        }
        // A name that is taken or not valid is refused, with the reason, and leaves nothing in the history.
        private async Task Rename(string newId, Action<string> rename, Func<string, Selection> renamed)
        {
            try
            {
                History?.Record();
                rename(newId?.Trim());
                Selection = renamed(newId.Trim());
                RenameError = null;
                StateChanged?.Invoke();
                await changed(Definition);
            }
            catch (ArgumentException e)
            {
                History?.Discard();
                RenameError = e.Message;
                StateChanged?.Invoke();
            }
        }

        public Task SetDeviceName(DeviceDefinition device, string name) => Mutate(() => device.Name = Blank(name));
        public Task SetMachineName(string name) => Mutate(() => Definition.Name = Blank(name));
        public Task SetProgramMemory(string id) => Mutate(() => Definition.ProgramMemory = Blank(id));
        public Task SetHalt(string id) => Mutate(() => Definition.Halt = Blank(id));

        public Task SetParameter(DeviceDefinition device, ParameterInfo parameter, string text)
        {
            return Mutate(() =>
            {
                if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out var value))
                {
                    device.Parameters.Remove(parameter.Name);
                    return;
                }
                value = Math.Clamp(value, parameter.Min, parameter.Max);
                using var document = JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                device.Parameters[parameter.Name] = document.RootElement.Clone();
            });
        }
        public static string ParameterText(DeviceDefinition device, ParameterInfo parameter)
        {
            return device.Parameters.TryGetValue(parameter.Name, out var value) ? value.ToString() : "";
        }
        public Task SetConnection(DeviceDefinition device, string connection, string targetId)
        {
            return Mutate(() =>
            {
                if (string.IsNullOrEmpty(targetId)) device.Connections.Remove(connection);
                else device.Connections[connection] = targetId;
            });
        }
        public Task SetPort(DeviceDefinition device, string port, string busId)
        {
            return Mutate(() => device.SetPortBus(port, Blank(busId)));
        }
        public Task SetDecoder(Action<DecoderDefinition> change)
        {
            return Mutate(() =>
            {
                Definition.Decoder ??= new DecoderDefinition();
                change(Definition.Decoder);
            });
        }
        // The decoder's sockets name the devices it works with.
        public Task SetDecoderTarget(string socket, string deviceId)
        {
            return SetDecoder(d =>
            {
                if (socket == "status") d.Status = deviceId;
                else if (socket == "instructionRegister") d.InstructionRegister = deviceId;
                else d.Interrupts = deviceId;
            });
        }
        public static string Blank(string value) => string.IsNullOrEmpty(value) ? null : value;
    }

    // A device the microcode uses, and where it uses it.
    public sealed record PendingDelete(string Id, List<(InstructionDefinition Instruction, int Step, string Signal)> Usages)
    {
        public List<string> Instructions => Usages.Select(u => u.Instruction.Mnemonic).Distinct().ToList();
    }
}
