using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
namespace Exuarch.Core
{
    // A microcoded machine built from a MachineDefinition: buses, devices, a decoder ROM and a program.
    public class Machine
    {
        public MachineDefinition Definition { get; }
        public IReadOnlyDictionary<string, Bus> Buses { get; }
        public IReadOnlyList<IBusDevice> Devices { get; }
        public DecoderRom DecoderRom { get; }
        public Assembler Assembler { get; }
        // The assembled program, one value per memory cell.
        public int[] ProgramByteCode { get; }
        public List<MicroInstruction> CurrentMicroCode { get; private set; }
        public int Cycles { get; private set; }
        private readonly Dictionary<string, IBusDevice> devicesByID;
        private readonly Bus[] busArray;
        private readonly IBusDevice[] deviceArray;
        // The buses each device is on, parallel to deviceArray: the only ones it can drive.
        private readonly Bus[][] busesOfDevice;
        private readonly bool[] alwaysClocked;
        // The devices in the roles the definition names (DeviceRole).
        private readonly InstructionRegister instructionRegister;
        private readonly Register statusRegister;
        private readonly InterruptController interrupts;
        private readonly Clock halt;
        private readonly MemoryModule programMemory;
        // The interrupt request as the decoder sees it: sampled when a fetch starts (micro step 0) and held for the
        // whole instruction, so a request can not switch micro routines halfway through one.
        private bool interruptSampled;
        private readonly SignalResolver signals;
        private readonly HistoryRecorder historyRecorder;
        private readonly LastTickRecorder lastTickRecorder = new LastTickRecorder();
        private ITickRecorder recorder;
        public const int HistoryLimit = 500;

        public Machine(MachineDefinition definition, string microcode, string source, DeviceRegistry registry = null)
            : this(definition, MicrocodeDefinition.Parse(microcode), source, registry)
        {
        }

        // Builds the machine with the microcode stored in its definition (decoder.microcode).
        public Machine(MachineDefinition definition, string source, DeviceRegistry registry = null)
            : this(definition, (MicrocodeDefinition)null, source, registry)
        {
        }

        // microcode overrides the definition's own decoder.microcode when given. The machine is built in stages, each
        // of which can stop it with the problems it finds: the definition is checked, the devices built and given
        // their roles, the microcode checked against them, and the program assembled and loaded.
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

        // The device in a role, checked to be of the kind the role needs, or null when the definition names none.
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

        // Stops at any error in the microcode, and returns the warnings.
        private List<MicrocodeDiagnostic> CheckMicrocode(MicrocodeDefinition microcode, DeviceRegistry registry)
        {
            var diagnostics = MicrocodeValidator.Validate(microcode, Definition, registry, this);
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
            if (errors.Count > 0) throw new MachineDefinitionException(errors);
            return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
        }

        // Register operands name the registers of the first register bank, if there is one.
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

        // Checks a definition on its own, without microcode or a program. Returns the problems found.
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

        // Microcode problems that do not stop the machine from running.
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
        // Runs ticks until the next instruction has been fetched into the micro step register, or the machine
        // halts. Returns the number of ticks run.
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

        // The last ticks, oldest first, at most HistoryLimit.
        public IReadOnlyList<TickRecord> History { get { return historyRecorder.History; } }
        public TickRecord LastTick { get; private set; }
        // Program memory address of the opcode last fetched into the micro step register.
        public int? CurrentInstructionAddress { get; private set; }

        // The instruction at an address as program memory holds it now: the assembled line while memory still holds what
        // the assembler put there, and otherwise what the cells decode to, with the operands as numbers. A program that
        // rewrites its own instructions or moves through memory runs code the assembler never saw. Null when the cell is
        // not an opcode.
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
        // What the decoder combines into the ROM address: the four flags in bits 0 to 3 and the sampled interrupt
        // request in bit 4 (FlagCondition.InterruptBit).
        public int DecoderStatus { get { return (statusRegister.Data & 0x0F) | (interruptSampled ? FlagCondition.InterruptBit : 0); } }
        // The decoder status the next tick will use: at micro step 0 the interrupt request is sampled afresh.
        public int NextDecoderStatus
        {
            get
            {
                bool request = instructionRegister.Data == 0 ? interrupts?.Requesting == true : interruptSampled;
                return (statusRegister.Data & 0x0F) | (request ? FlagCondition.InterruptBit : 0);
            }
        }
        public InterruptController Interrupts { get { return interrupts; } }
        public int MicroStepRegister { get { return instructionRegister.Data; } }
        // The instruction and micro step the next tick will run.
        public (InstructionDefinition Instruction, MicroStep Step, int Offset)? NextStep
        {
            get { return DecoderRom.Locate(NextDecoderStatus, instructionRegister.Data); }
        }

        // Plans by decoder status and micro step for the steps in the ROM's blocks, and by ROM address for any other.
        private readonly TickPlan[][] plansByStatus;
        private readonly Dictionary<int, TickPlan> plans = new Dictionary<int, TickPlan>();

        // When false, ticks skip the detail kept for display (bus transfers, value changes, memory writes and the
        // history), which makes running much faster. LastTick, breakpoints and CurrentInstructionAddress still work.
        public bool RecordHistory
        {
            get { return recorder == historyRecorder; }
            set { recorder = value ? historyRecorder : lastTickRecorder; }
        }

        // Every tick looks its plan up here, so this part must not allocate; building a new plan, with its lambdas and
        // their closures, is a method of its own.
        private TickPlan PlanFor(int status, int step)
        {
            var byStep = plansByStatus[status];
            if (step < byStep.Length) return byStep[step] ??= BuildPlan(status, step);
            int address = DecoderRom.RomAddress(status, step);
            if (!plans.TryGetValue(address, out var plan)) plans[address] = plan = BuildPlan(status, step);
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
                // An instruction is fetched when the micro step register loads an opcode that program memory puts out.
                // Loading it from anywhere else is a jump inside the microcode, such as WORM-16's HATCH loop.
                LoadsInstruction = microCode.Any(m => m.DeviceID == Definition.Decoder.InstructionRegister && m.Function == "load")
                    && microCode.Any(m => m.DeviceID == Definition.ProgramMemory && m.Function == "output"),
            };
            var located = DecoderRom.Locate(status, step);
            plan.Instruction = located?.Instruction.Mnemonic;
            if (located?.Step != null) plan.StepIndex = located.Value.Instruction.Steps.IndexOf(located.Value.Step);
            return plan;
        }
        // The devices that take a value from the bus in a tick with these signals.
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

        // Builds devices on demand so connections may refer to devices defined later in the list.
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
