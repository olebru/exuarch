using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
namespace Exuarch.Core
{
    public class Machine
    {
        public MachineDefinition Definition { get; }
        public IReadOnlyDictionary<string, Bus> Buses { get; }
        public IReadOnlyList<IBusDevice> Devices { get; }
        public DecoderRom DecoderRom { get; }
        public Assembler Assembler { get; }
        public int[] ProgramByteCode { get; }
        public List<MicroInstruction> CurrentMicroCode { get; private set; }
        public int Cycles { get; private set; }
        private const int StatusFlagBits = 0x0F;
        private readonly Dictionary<string, IBusDevice> devicesByID;
        private readonly Bus[] busArray;
        private readonly IBusDevice[] deviceArray;
        private readonly Bus[][] busesOfDevice;
        private readonly bool[] alwaysClocked;
        private readonly InstructionRegister instructionRegister;
        private readonly Register statusRegister;
        private readonly InterruptController interrupts;
        private readonly Clock halt;
        private readonly MemoryModule programMemory;
        private bool interruptSampled;
        private readonly SignalResolver signals;
        private readonly HistoryRecorder historyRecorder;
        private readonly LastTickRecorder lastTickRecorder = new LastTickRecorder();
        private ITickRecorder recorder;
        public const int HistoryLimit = 10_000;

        public Machine(MachineDefinition definition, string microcode, string source, DeviceRegistry registry = null)
            : this(definition, MicrocodeDefinition.Parse(microcode), source, registry)
        {
        }

        public Machine(MachineDefinition definition, string source, DeviceRegistry registry = null)
            : this(definition, (MicrocodeDefinition)null, source, registry)
        {
        }

        public Machine(MachineDefinition definition, MicrocodeDefinition microcode, string source, DeviceRegistry registry = null)
        {
            registry ??= DeviceRegistry.CreateDefault();
            Definition = definition;
            DefinitionRules.Validate(definition, registry);

            Buses = definition.Buses.ToDictionary(b => b.Id, b => new Bus(b.Id));
            devicesByID = BuildDevices(definition, registry, Buses);
            Devices = definition.Devices.Select(d => devicesByID[d.Id]).ToList();
            AttachToBuses();
            busArray = Buses.Values.ToArray();
            deviceArray = Devices.ToArray();
            busesOfDevice = deviceArray.Select(d => busArray.Where(b => b.devices.Contains(d)).ToArray()).ToArray();
            alwaysClocked = AlwaysClocked();

            statusRegister = Role<Register>(DeviceRole.Status);
            interrupts = Role<InterruptController>(DeviceRole.Interrupts);
            instructionRegister = Role<InstructionRegister>(DeviceRole.InstructionRegister);
            halt = Role<Clock>(DeviceRole.Halt);
            programMemory = Role<MemoryModule>(DeviceRole.ProgramMemory);

            signals = new SignalResolver(definition, registry);
            microcode = MicrocodeOf(microcode);
            MicrocodeWarnings = CheckMicrocode(microcode, registry);
            DecoderRom = new DecoderRom(microcode);
            ConnectInstructionFormat();
            plansByStatus = Enumerable.Range(0, DecoderRom.StatusVariants).Select(_ => new TickPlan[DecoderRom.OpCodesUsed]).ToArray();
            Assembler = CreateAssembler();
            ProgramByteCode = Assembler.Assemble(source ?? string.Empty);
            LoadProgram();
            CurrentMicroCode = DecoderRom.FetchInstruction(NextDecoderStatus, instructionRegister.Data);
            recorder = historyRecorder = new HistoryRecorder(busArray, Devices);
        }

        private void ConnectInstructionFormat()
        {
            var format = DecoderRom.Format;
            if (format != null) instructionRegister.Dispatch = DecoderRom.StepFor;
            foreach (var word in Devices.OfType<InstructionWord>()) word.Format = format;
        }

        private bool[] AlwaysClocked()
        {
            var mastered = Definition.Devices.Where(d => devicesByID[d.Id] is IBusMaster).SelectMany(d => d.Connections.Values).ToHashSet();
            return deviceArray.Select(d => d is not IPassiveDevice || mastered.Contains(d.ID())).ToArray();
        }

        private int[] ClockedIn(List<MicroInstruction> microCode)
        {
            var enabled = microCode.Select(m => m.DeviceID).ToHashSet();
            return Enumerable.Range(0, deviceArray.Length).Where(i => alwaysClocked[i] || enabled.Contains(deviceArray[i].ID())).ToArray();
        }

        private void AttachToBuses()
        {
            foreach (var deviceDefinition in Definition.Devices)
            {
                foreach (var busId in deviceDefinition.Ports().Select(p => p.Value).Distinct())
                {
                    Buses[busId].devices.Add(devicesByID[deviceDefinition.Id]);
                }
            }
        }

        private T Role<T>(DeviceRole role) where T : class, IBusDevice
        {
            var id = role.DeviceIn(Definition);
            return id == null ? null : Device<T>(id, role.Setting);
        }

        private MicrocodeDefinition MicrocodeOf(MicrocodeDefinition microcode)
        {
            return microcode ?? Definition.Decoder.Microcode
                ?? throw new MachineDefinitionException("\"decoder.microcode\" is required: the fetch routine and instructions for this machine.");
        }

        private List<MicrocodeDiagnostic> CheckMicrocode(MicrocodeDefinition microcode, DeviceRegistry registry)
        {
            var diagnostics = MicrocodeValidator.Validate(microcode, Definition, registry, this);
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
            if (errors.Count > 0) throw new MachineDefinitionException(errors);
            return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
        }

        private Assembler CreateAssembler()
        {
            var registers = Devices.OfType<IRegisterBank>().FirstOrDefault();
            return new Assembler(DecoderRom, programMemory?.Size ?? MemoryModule.DefaultSize) { RegisterCount = registers?.Count ?? 0 };
        }

        private void LoadProgram()
        {
            if (ProgramByteCode.Length == 0) return;
            if (programMemory == null) throw new MachineDefinitionException("\"programMemory\" must be set to load a program.");
            programMemory.LoadProgram(ProgramByteCode);
        }

        public static Machine FromJson(string definitionJson, string microcode, string source, DeviceRegistry registry = null)
        {
            return new Machine(MachineDefinition.FromJson(definitionJson), microcode, source, registry);
        }
        public static Machine FromJson(string definitionJson, string source, DeviceRegistry registry = null)
        {
            return new Machine(MachineDefinition.FromJson(definitionJson), source, registry);
        }
        public static Machine CreateExample()
        {
            return FromJson(ExampleData.MACHINE, ExampleData.SRC);
        }

        public static IReadOnlyList<string> ValidateDefinition(MachineDefinition definition, DeviceRegistry registry = null)
        {
            try
            {
                new Machine(definition, new MicrocodeDefinition { Fetch = new InstructionDefinition { Mnemonic = "FTC", Steps = { new MicroStep() } } }, "", registry);
                return Array.Empty<string>();
            }
            catch (MachineDefinitionException e)
            {
                return e.Errors.Where(error => !error.StartsWith("Microcode")).ToList();
            }
        }

        public IReadOnlyList<MicrocodeDiagnostic> MicrocodeWarnings { get; }

        public bool IsHalted { get { return halt != null && halt.IsHalted(); } }
        public IBusDevice Device(string id)
        {
            return devicesByID.TryGetValue(id, out var device) ? device : null;
        }
        public T Device<T>(string id) where T : class, IBusDevice
        {
            return Device(id) as T;
        }

        public void SingleStep()
        {
            if (!IsHalted)
            {
                Step();
            }
        }
        public IEnumerable<int> Run()
        {
            while (!IsHalted)
            {
                Step();
                yield return Cycles;
            }
        }
        public int StepInstruction(int maxTicks = 10000)
        {
            int ticks = 0;
            while (!IsHalted && ticks < maxTicks)
            {
                Step();
                ticks++;
                if (LastTick.FetchedFromAddress != null) break;
            }
            return ticks;
        }

        public IReadOnlyList<TickRecord> History { get { return historyRecorder.History; } }
        public TickRecord LastTick { get; private set; }
        public int? CurrentInstructionAddress { get; private set; }

        public ListingLine InstructionAt(int address)
        {
            var assembled = Assembler.Listing.FirstOrDefault(l => l.IsInstruction && l.Address == address);
            if (programMemory == null) return assembled;
            int Cell(int offset) => programMemory.ValueAt((address + offset) % programMemory.Size);
            if (assembled != null && assembled.Cells.Select((value, i) => Cell(i) == value).All(same => same)) return assembled;
            var instruction = DecoderRom.InstructionOfWord(Cell(0));
            if (instruction == null) return null;
            var (cells, shown) = InstructionFormat.Disassemble(DecoderRom.Format, instruction, Cell);
            return new ListingLine
            {
                Address = address,
                Label = assembled?.Label,
                Mnemonic = instruction.Mnemonic,
                Operands = shown,
                Cells = cells,
                IsInstruction = true,
                LineNumber = assembled?.LineNumber ?? 0,
            };
        }
        public int Status { get { return statusRegister.Data; } }
        public int DecoderStatus { get { return (statusRegister.Data & StatusFlagBits) | (interruptSampled ? FlagCondition.InterruptBit : 0); } }
        public int NextDecoderStatus
        {
            get
            {
                bool request = instructionRegister.Data == 0 ? interrupts?.Requesting == true : interruptSampled;
                return (statusRegister.Data & StatusFlagBits) | (request ? FlagCondition.InterruptBit : 0);
            }
        }
        public InterruptController Interrupts { get { return interrupts; } }
        public int MicroStepRegister { get { return instructionRegister.Data; } }
        public (InstructionDefinition Instruction, MicroStep Step, int Offset)? NextStep
        {
            get { return DecoderRom.Locate(NextDecoderStatus, instructionRegister.Data); }
        }

        private readonly TickPlan[][] plansByStatus;
        private readonly Dictionary<int, TickPlan> plansByRomAddress = new Dictionary<int, TickPlan>();

        public bool RecordHistory
        {
            get { return recorder == historyRecorder; }
            set { recorder = value ? historyRecorder : lastTickRecorder; }
        }

        public bool TraceTicks
        {
            get { return historyRecorder.KeepsRecords; }
            set { historyRecorder.KeepsRecords = value; }
        }

        private TickPlan PlanFor(int status, int step)
        {
            var byStep = plansByStatus[status];
            if (step < byStep.Length) return byStep[step] ??= BuildPlan(status, step);
            int address = DecoderRom.RomAddress(status, step);
            if (!plansByRomAddress.TryGetValue(address, out var plan)) plansByRomAddress[address] = plan = BuildPlan(status, step);
            return plan;
        }

        private TickPlan BuildPlan(int status, int step)
        {
            int address = DecoderRom.RomAddress(status, step);
            var microCode = DecoderRom.FetchInstruction(status, step);
            var signalTexts = microCode.Select(m => $"{m.DeviceID}.{m.Function}").ToArray();
            var plan = new TickPlan
            {
                RomAddress = address,
                MicroCode = microCode,
                Lines = microCode.Select(m => ControlLineTable.Bind(devicesByID[m.DeviceID], m.Function)).ToArray(),
                Clocked = ClockedIn(microCode),
                Signals = signalTexts,
                Readers = busArray.Select(bus => ReadersOf(bus.ID, signalTexts)).ToArray(),
                LoadsInstruction = microCode.Any(m => m.DeviceID == Definition.Decoder.InstructionRegister && m.Function == "load")
                    && microCode.Any(m => m.DeviceID == Definition.ProgramMemory && m.Function == "output"),
            };
            var located = DecoderRom.Locate(status, step);
            plan.Instruction = located?.Instruction.Mnemonic;
            if (located?.Step != null) plan.StepIndex = located.Value.Instruction.Steps.IndexOf(located.Value.Step);
            return plan;
        }
        private List<string> ReadersOf(string busId, IEnumerable<string> signalTexts)
        {
            return signalTexts.Select(signals.Resolve).Where(s => s.ReadBus == busId).Select(s => s.Signal.Device).Distinct().ToList();
        }

        private void Step()
        {
            if (instructionRegister.Data == 0) interruptSampled = interrupts?.Requesting == true;
            int status = DecoderStatus;
            var plan = PlanFor(status, instructionRegister.Data);
            var record = Begin(plan, status);
            CurrentMicroCode = plan.MicroCode;
            plan.Enable();
            Clocking.Tick(busArray, deviceArray, busesOfDevice, plan.Clocked);
            Cycles++;
            if (plan.LoadsInstruction) CurrentInstructionAddress = record.FetchedFromAddress = programMemory.memoryAddress;
            recorder.End(record, plan);
            LastTick = record;
        }
        private TickRecord Begin(TickPlan plan, int status)
        {
            var record = recorder.Begin();
            record.Cycle = Cycles + 1;
            record.Status = status;
            record.MicroStep = instructionRegister.Data;
            record.RomAddress = plan.RomAddress;
            record.Instruction = plan.Instruction;
            record.StepIndex = plan.StepIndex;
            record.FetchedFromAddress = null;
            return record;
        }

        private T Device<T>(string id, string setting) where T : class, IBusDevice
        {
            var device = Device(id);
            string Article(string name) => "AEIOU".Contains(name[0]) ? "an" : "a";
            return device as T ?? throw new MachineDefinitionException(
                $"\"{setting}\" must name {Article(typeof(T).Name)} {typeof(T).Name} device, but '{id}' is {Article(device.GetType().Name)} {device.GetType().Name}.");
        }

        private static Dictionary<string, IBusDevice> BuildDevices(MachineDefinition definition, DeviceRegistry registry, IReadOnlyDictionary<string, Bus> buses)
        {
            var definitions = definition.Devices.ToDictionary(d => d.Id);
            var built = new Dictionary<string, IBusDevice>();
            var building = new Stack<string>();
            IBusDevice Build(string id)
            {
                if (built.TryGetValue(id, out var existing)) return existing;
                if (building.Contains(id))
                {
                    throw new MachineDefinitionException($"Device connections form a cycle: {string.Join(" -> ", building.Reverse().Append(id))}.");
                }
                building.Push(id);
                var deviceDefinition = definitions[id];
                var device = registry.Factory(deviceDefinition.Type)(new DeviceBuildContext(deviceDefinition, buses, Build));
                building.Pop();
                if (device.ID() != id)
                {
                    throw new MachineDefinitionException($"Device '{id}': factory for type '{deviceDefinition.Type}' returned a device with ID '{device.ID()}'.");
                }
                built[id] = device;
                return device;
            }
            foreach (var deviceDefinition in definition.Devices) Build(deviceDefinition.Id);
            return built;
        }

    }
}
