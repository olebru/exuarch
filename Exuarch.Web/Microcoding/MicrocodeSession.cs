using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Microcoding
{
    // The microcode being written and where the editor is in it: the instruction picked, the step being edited, the
    // flags the preview runs with, the filters and what is being dragged. Every edit goes through here: it is recorded
    // in the undo history shared with the machine editor and handed to the page, which validates the microcode again.
    public sealed class MicrocodeSession
    {
        public static readonly (string Name, int Flag)[] Flags =
        {
            ("N", StatusRegister.NegativeFlag), ("V", StatusRegister.OverflowFlag), ("C", StatusRegister.CarryFlag), ("Z", StatusRegister.ZeroFlag),
            ("I", FlagCondition.InterruptBit),
        };

        private readonly Func<MicrocodeDefinition, Task> changed;
        private readonly Func<string, Task> openDevice;
        private string selectedMnemonic;
        private FocusRequest focusSeen = FocusRequest.None;
        private DragPayload dragged;

        public MicrocodeSession(Func<MicrocodeDefinition, Task> changed, Func<string, Task> openDevice)
        {
            this.changed = changed;
            this.openDevice = openDevice;
        }

        // Something only the editor shows changed, such as the step being edited.
        public event Action StateChanged;

        public MicrocodeDefinition Microcode { get; private set; }
        public MachineDefinition Machine { get; private set; }
        public IReadOnlyList<MicrocodeDiagnostic> Diagnostics { get; private set; } = Array.Empty<MicrocodeDiagnostic>();
        public EditHistory History { get; private set; }
        public SignalCatalog Signals { get; private set; }

        public int ActiveStep { get; private set; }
        public string Filter { get; set; } = "";
        public string PaletteFilter { get; set; } = "";
        public string NewSignal { get; set; } = "";
        public string RenameError { get; private set; }
        // The flags the steps are previewed with; null runs every step.
        public int? PreviewStatus { get; private set; }

        // The editor's parameters, every time the page renders it. A request to show an instruction picks it, unless
        // the microcode has no such instruction; otherwise the instruction stays picked while it exists.
        public void Update(MicrocodeDefinition microcode, MachineDefinition machine, IReadOnlyList<MicrocodeDiagnostic> diagnostics, DeviceRegistry registry, EditHistory history, FocusRequest focus)
        {
            Microcode = microcode;
            Machine = machine;
            Diagnostics = diagnostics;
            History = history;
            Signals = new SignalCatalog(machine, registry);
            if (microcode == null) return;
            if (focus != focusSeen)
            {
                focusSeen = focus;
                if (Focus(focus)) return;
            }
            if (Selected == null)
            {
                selectedMnemonic = microcode.Fetch?.Mnemonic ?? microcode.Instructions.FirstOrDefault()?.Mnemonic;
                ActiveStep = 0;
            }
        }
        private bool Focus(FocusRequest focus)
        {
            if (focus.Target == null || Microcode.FindInstruction(focus.Target) == null) return false;
            selectedMnemonic = focus.Target;
            ActiveStep = focus.Step;
            Filter = "";
            return true;
        }

        public InstructionDefinition Selected => selectedMnemonic == null ? null : Microcode.FindInstruction(selectedMnemonic);
        public bool SelectedIsFetch => Selected != null && Selected == Microcode.Fetch;

        // ---- Diagnostics ----

        public IEnumerable<MicrocodeDiagnostic> DiagnosticsFor(InstructionDefinition instruction, int? step = null)
        {
            return Diagnostics.Where(d => d.Instruction == instruction.Mnemonic && (step == null || d.Step == step));
        }
        public bool SignalHasProblem(InstructionDefinition instruction, int step, string signal)
        {
            return Diagnostics.Any(d => d.Instruction == instruction.Mnemonic && d.Step == step && d.Signal == signal);
        }
        public int ErrorCount => DiagnosticStyle.Errors(Diagnostics);
        public int WarningCount => DiagnosticStyle.Warnings(Diagnostics);

        // A problem in the list of problems: show its instruction, at its step.
        public void Show(MicrocodeDiagnostic diagnostic)
        {
            var instruction = diagnostic.Instruction == null ? null : Microcode.FindInstruction(diagnostic.Instruction);
            if (instruction == null) return;
            Select(instruction);
            if (diagnostic.Step != null) Activate(diagnostic.Step.Value);
        }

        // A signal's device, shown in the machine editor.
        public Task OpenDevice(string signalText)
        {
            return Signal.TryParse(signalText, out var signal) && Machine?.FindDevice(signal.Device) != null
                ? openDevice(signal.Device)
                : Task.CompletedTask;
        }

        // ---- Picking ----

        public void Select(InstructionDefinition instruction)
        {
            selectedMnemonic = instruction.Mnemonic;
            ActiveStep = 0;
            RenameError = null;
            NewSignal = "";
            StateChanged?.Invoke();
        }
        public void Activate(int step)
        {
            ActiveStep = step;
            StateChanged?.Invoke();
        }
        // The list's filter, on the mnemonic and the description.
        public bool Matches(InstructionDefinition instruction)
        {
            return Filter.Length == 0 || instruction.Mnemonic.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || (instruction.Description ?? "").Contains(Filter, StringComparison.OrdinalIgnoreCase);
        }

        // ---- The flag preview ----

        public void ClearPreview()
        {
            PreviewStatus = null;
            StateChanged?.Invoke();
        }
        public void TogglePreview(int flag)
        {
            PreviewStatus = (PreviewStatus ?? 0) ^ flag;
            StateChanged?.Invoke();
        }
        public bool PreviewHas(int flag) => PreviewStatus.HasValue && (PreviewStatus.Value & flag) != 0;
        public bool Runs(MicroStep step) => PreviewStatus == null || step.AppliesTo(PreviewStatus.Value);
        // The tick a step runs in with the preview's flags; null when it does not run or every step is shown.
        public int? TickOf(InstructionDefinition instruction, MicroStep step)
        {
            if (PreviewStatus == null) return null;
            var index = instruction.StepsFor(PreviewStatus.Value).IndexOf(step);
            return index < 0 ? null : index;
        }

        // ---- Editing ----

        private async Task Mutate(Action change)
        {
            History?.Record();
            change();
            await changed(Microcode);
        }
        public Task Undo() => History?.Undo() ?? Task.CompletedTask;
        public Task Redo() => History?.Redo() ?? Task.CompletedTask;
        public bool CanUndo => History?.CanUndo == true;
        public bool CanRedo => History?.CanRedo == true;

        // Microcode read from a file in place of this.
        public async Task Replace(MicrocodeDefinition microcode)
        {
            History?.Record();
            Microcode = microcode;
            if (microcode.FindInstruction(selectedMnemonic) == null) selectedMnemonic = microcode.Fetch?.Mnemonic;
            await changed(microcode);
        }

        public async Task AddInstruction()
        {
            var mnemonic = "NEW";
            for (int i = 2; Microcode.FindInstruction(mnemonic) != null; i++) mnemonic = $"NEW{i}";
            var stepRegister = Machine?.Decoder?.InstructionRegister;
            var instruction = new InstructionDefinition { Mnemonic = mnemonic, Operands = 0, Steps = { new MicroStep() } };
            if (stepRegister != null) instruction.Steps[0].Signals.Add($"{stepRegister}.reset");
            await Mutate(() => Microcode.Instructions.Add(instruction));
            Select(instruction);
        }
        public async Task DuplicateInstruction()
        {
            var source = Selected;
            if (source == null || SelectedIsFetch) return;
            var copy = Microcode.Clone().FindInstruction(source.Mnemonic);
            var mnemonic = source.Mnemonic + "2";
            for (int i = 3; Microcode.FindInstruction(mnemonic) != null; i++) mnemonic = source.Mnemonic + i;
            copy.Mnemonic = mnemonic;
            await Mutate(() => Microcode.Instructions.Insert(Microcode.Instructions.IndexOf(source) + 1, copy));
            Select(copy);
        }
        public async Task DeleteInstruction()
        {
            var instruction = Selected;
            if (instruction == null || SelectedIsFetch) return;
            var index = Microcode.Instructions.IndexOf(instruction);
            await Mutate(() => Microcode.Instructions.Remove(instruction));
            var next = Microcode.Instructions.ElementAtOrDefault(Math.Min(index, Microcode.Instructions.Count - 1)) ?? Microcode.Fetch;
            if (next != null) Select(next);
        }
        public Task MoveInstruction(int from, int to)
        {
            return Mutate(() =>
            {
                var item = Microcode.Instructions[from];
                Microcode.Instructions.RemoveAt(from);
                Microcode.Instructions.Insert(to, item);
            });
        }
        public async Task Rename(string value)
        {
            var mnemonic = value?.Trim().ToUpperInvariant();
            RenameError = RenameProblem(mnemonic);
            StateChanged?.Invoke();
            if (RenameError != null || mnemonic == selectedMnemonic) return;
            var instruction = Selected;
            await Mutate(() => instruction.Mnemonic = mnemonic);
            selectedMnemonic = mnemonic;
        }
        private string RenameProblem(string mnemonic)
        {
            if (string.IsNullOrEmpty(mnemonic) || mnemonic.Any(char.IsWhiteSpace)) return "Mnemonic must be a single word.";
            if (mnemonic != selectedMnemonic && Microcode.FindInstruction(mnemonic) != null) return $"'{mnemonic}' is already defined.";
            return null;
        }
        // Changing the count keeps the operand types in step: extra ones are dropped, new ones start as values.
        public Task SetOperands(string value)
        {
            var instruction = Selected;
            return Mutate(() =>
            {
                instruction.Operands = int.TryParse(value, out var n) && n >= 0 ? n : null;
                if (instruction.OperandTypes == null) return;
                if (instruction.Operands == null || instruction.Operands == 0) { instruction.OperandTypes = null; return; }
                if (instruction.OperandTypes.Count > instruction.Operands) instruction.OperandTypes.RemoveRange(instruction.Operands.Value, instruction.OperandTypes.Count - instruction.Operands.Value);
                while (instruction.OperandTypes.Count < instruction.Operands) instruction.OperandTypes.Add(OperandType.Value);
            });
        }
        // Sets one operand's type. Clearing a type removes the list, so the instruction simply does not say.
        public Task SetOperandType(int index, string value)
        {
            var instruction = Selected;
            return Mutate(() =>
            {
                int count = instruction.OperandCount ?? 0;
                if (!Enum.TryParse<OperandType>(value, out var type))
                {
                    instruction.OperandTypes = null;
                    return;
                }
                instruction.OperandTypes ??= Enumerable.Repeat(OperandType.Value, count).ToList();
                while (instruction.OperandTypes.Count < count) instruction.OperandTypes.Add(OperandType.Value);
                instruction.OperandTypes[index] = type;
            });
        }
        public Task SetDescription(string value)
        {
            var instruction = Selected;
            return Mutate(() => instruction.Description = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        public async Task AddStep()
        {
            var instruction = Selected;
            await Mutate(() => instruction.Steps.Add(new MicroStep()));
            ActiveStep = instruction.Steps.Count - 1;
        }
        public async Task DuplicateStep(int index)
        {
            var instruction = Selected;
            var source = instruction.Steps[index];
            await Mutate(() => instruction.Steps.Insert(index + 1, new MicroStep
            {
                When = source.When == null ? null : FlagCondition.FromPattern(FlagCondition.ToPattern(source.When)),
                Signals = source.Signals.ToList(),
                Comment = source.Comment,
            }));
            ActiveStep = index + 1;
        }
        public async Task DeleteStep(int index)
        {
            var instruction = Selected;
            await Mutate(() => instruction.Steps.RemoveAt(index));
            ActiveStep = Math.Max(0, Math.Min(ActiveStep, instruction.Steps.Count - 1));
        }
        public Task MoveStep(int from, int to)
        {
            var instruction = Selected;
            if (from == to || to < 0 || to >= instruction.Steps.Count) return Task.CompletedTask;
            ActiveStep = to;
            return Mutate(() =>
            {
                var step = instruction.Steps[from];
                instruction.Steps.RemoveAt(from);
                instruction.Steps.Insert(to, step);
            });
        }
        // Cycles a flag condition: any -> set -> clear -> any.
        public Task CycleFlag(MicroStep step, string flag)
        {
            return Mutate(() =>
            {
                var pattern = FlagCondition.ToPattern(step.When).ToCharArray();
                int index = Array.FindIndex(Flags, f => f.Name == flag);
                pattern[index] = pattern[index] == 'x' ? '1' : pattern[index] == '1' ? '0' : 'x';
                step.When = FlagCondition.FromPattern(new string(pattern));
            });
        }
        // What a step needs of a flag: "x" for any, "1" or "0".
        public static string FlagState(MicroStep step, string flag)
        {
            var pattern = FlagCondition.ToPattern(step.When);
            return pattern[Array.FindIndex(Flags, f => f.Name == flag)].ToString();
        }
        public Task SetComment(MicroStep step, string value)
        {
            return Mutate(() => step.Comment = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        public async Task AddSignal(int stepIndex, string signal)
        {
            signal = signal?.Trim();
            var instruction = Selected;
            if (string.IsNullOrEmpty(signal) || instruction == null || stepIndex < 0 || stepIndex >= instruction.Steps.Count) return;
            var step = instruction.Steps[stepIndex];
            if (step.Signals.Contains(signal)) return;
            await Mutate(() => step.Signals.Add(signal));
            ActiveStep = stepIndex;
        }
        // What was typed in the step's field, added to it.
        public async Task AddNewSignal(int stepIndex)
        {
            var signal = NewSignal;
            NewSignal = "";
            await AddSignal(stepIndex, signal);
        }
        public Task RemoveSignal(int stepIndex, string signal)
        {
            var step = Selected.Steps[stepIndex];
            return Mutate(() => step.Signals.Remove(signal));
        }
        public async Task MoveSignal(string signal, int from, int to)
        {
            var instruction = Selected;
            var target = instruction.Steps[to];
            await Mutate(() =>
            {
                instruction.Steps[from].Signals.Remove(signal);
                if (!target.Signals.Contains(signal)) target.Signals.Add(signal);
            });
            ActiveStep = to;
        }
        // A control line clicked in the palette goes into the step being edited, or a new one.
        public async Task AddFromPalette(string signal)
        {
            if (Selected == null) return;
            if (Selected.Steps.Count == 0) await AddStep();
            await AddSignal(Math.Min(ActiveStep, Selected.Steps.Count - 1), signal);
        }

        // ---- Drag and drop (HTML5) ----

        public void Drag(DragPayload payload)
        {
            dragged = payload;
        }
        public Task DropOnInstruction(int index) => Take()?.DropOnInstruction(this, index) ?? Task.CompletedTask;
        public Task DropOnStep(int step) => Take()?.DropOnStep(this, step) ?? Task.CompletedTask;
        public Task DropOnNewStep() => Take()?.DropOnNewStep(this) ?? Task.CompletedTask;
        private DragPayload Take()
        {
            var payload = dragged;
            dragged = null;
            return payload;
        }
    }
}
