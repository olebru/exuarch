using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Exuarch.Core;

namespace Exuarch.Core.Tests.Snapshots;

// What the assembler and the validators make of every built in package and its programs, and the validators of broken
// versions of each machine and its microcode.
public class ToolchainSnapshotTests
{
    public static TheoryData<string> Packages()
    {
        var data = new TheoryData<string>();
        // The examples; the tutorial starts are built from TutorialMachines, which TutorialTests checks.
        foreach (var package in BuiltInPackages.Examples) data.Add(package.Name);
        return data;
    }

    private static AssemblyLanguage Language(MachinePackage package)
    {
        return new AssemblyLanguage(package.Machine.Decoder.Microcode, ProgramMemorySize(package.Machine), RegisterFile.CountIn(package.Machine));
    }

    private static int ProgramMemorySize(MachineDefinition machine)
    {
        try
        {
            var built = new Machine(machine, "");
            var id = machine.ProgramMemory ?? machine.Devices.FirstOrDefault(d => built.Device(d.Id) is MemoryModule or MMU)?.Id;
            return built.Device(id) switch
            {
                MemoryModule memory => memory.Size,
                MMU mmu => mmu.SelectedBank.Size,
                _ => MemoryModule.DefaultSize,
            };
        }
        catch (MachineDefinitionException)
        {
            return MemoryModule.DefaultSize;
        }
    }

    private static string Describe(AssemblyResult result)
    {
        var text = new StringBuilder();
        foreach (var d in result.Diagnostics) text.Append($"  {d.Severity} line {d.Line} col {d.StartColumn}-{d.EndColumn}: {d.Message}\n");
        foreach (var label in result.Labels.OrderBy(l => l.Key, StringComparer.Ordinal)) text.Append($"  label {label.Key} = {label.Value}\n");
        text.Append("  cells");
        for (int i = 0; i < result.Cells.Length; i++) text.Append(i % 16 == 0 ? $"\n    {i:X4}:" : "").Append($" {result.Cells[i]:X4}");
        text.Append('\n');
        foreach (var line in result.Listing)
        {
            text.Append($"  L{line.LineNumber} @{line.Address} {(line.IsInstruction ? "I" : "-")} {line.Label}|{line.Mnemonic}|{string.Join(",", line.Operands ?? Array.Empty<string>())}|{string.Join(" ", (line.Cells ?? Array.Empty<int>()).Select(c => c.ToString("X4")))}\n");
        }
        return text.ToString();
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Assembly(string name)
    {
        var package = BuiltInPackages.Get(name);
        var language = Language(package);
        var text = new StringBuilder();
        foreach (var program in package.Programs)
        {
            text.Append($"== {program.Name}\n");
            text.Append(Describe(language.Analyze(program.Source)));
            text.Append($"  formatted sha {Hash(AssemblyLanguage.Format(program.Source))}\n");
            var built = new Machine(package.Machine, program.Source);
            text.Append($"  bytecode {string.Join(" ", built.ProgramByteCode.Take(64).Select(c => c.ToString("X4")))} ({built.ProgramByteCode.Length})\n");
        }
        Snapshot.Match($"assembly-{name}", text.ToString());
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Microcode(string name)
    {
        var package = BuiltInPackages.Get(name);
        var machine = package.Machine;
        var microcode = machine.Decoder.Microcode;
        var text = new StringBuilder();
        text.Append(Diagnostics("shipped", MicrocodeValidator.Validate(microcode, machine)));
        foreach (var instruction in microcode.AllInstructions)
        {
            text.Append($"  {instruction.Mnemonic} {instruction.Signature} ops={instruction.OperandCount}");
            foreach (var step in instruction.Steps) text.Append($" [{step.When} {FlagCondition.ToPattern(step.When)} {string.Join("+", step.Signals)}]");
            text.Append('\n');
        }
        for (int status = 0; status < 32; status++)
        {
            text.Append($"  status {status:X2}: {string.Join(" ", microcode.AllInstructions.Select(i => i.StepsFor(status).Count))}\n");
        }
        var rom = new DecoderRom(microcode);
        text.Append($"  rom {DescribeRom(rom)}\n");

        // Each instruction broken in the ways the validator looks for.
        var signals = microcode.AllInstructions.SelectMany(i => i.Steps).SelectMany(s => s.Signals).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var mutations = new List<(string Label, Action<MicrocodeDefinition> Change)>
        {
            ("unknown signal", m => m.Fetch.Steps[0].Signals.Add("nosuch.load")),
            ("unknown line", m => m.Fetch.Steps[0].Signals.Add(machine.Devices[0].Id + ".nosuchline")),
            ("bad signal text", m => m.Fetch.Steps[0].Signals.Add("no-dot")),
            ("duplicate signal", m => m.Fetch.Steps[0].Signals.Add(m.Fetch.Steps[0].Signals.FirstOrDefault() ?? "x.y")),
            ("empty fetch", m => m.Fetch.Steps.Clear()),
            ("duplicate mnemonic", m => { if (m.Instructions.Count > 0) m.Instructions.Add(MicrocodeDefinition.FromJson(m.ToJson()).Instructions[0]); }),
            ("no steps", m => { if (m.Instructions.Count > 0) m.Instructions[0].Steps.Clear(); }),
            ("bad mnemonic", m => { if (m.Instructions.Count > 0) m.Instructions[0].Mnemonic = "1 bad"; }),
            ("no end", m => { foreach (var i in m.Instructions) i.Steps.RemoveAll(s => s.Signals.Any(x => x.EndsWith(".reset"))); }),
            ("too many operands", m => { if (m.Instructions.Count > 0) m.Instructions[0].Operands = 9; }),
            ("condition", m => { if (m.Instructions.Count > 0) m.Instructions[0].Steps.Add(new MicroStep { When = FlagCondition.FromPattern("1-0-") }); }),
        };
        foreach (var a in signals.Take(30))
        {
            foreach (var b in signals.Take(30)) mutations.Add(($"{a} with {b}", m => m.Fetch.Steps[0].Signals.AddRange(new[] { a, b })));
        }
        foreach (var (label, change) in mutations)
        {
            var copy = microcode.Clone();
            try
            {
                change(copy);
                text.Append(Diagnostics(label, MicrocodeValidator.Validate(copy, machine)));
            }
            catch (Exception e)
            {
                text.Append($"-- {label}: threw {e.GetType().Name}: {e.Message}\n");
            }
        }
        Snapshot.Match($"microcode-{name}", text.ToString());
    }

    private static string DescribeRom(DecoderRom rom)
    {
        var text = new StringBuilder($"{rom.OpCodesUsed} used, {rom.OpCodeAddressSpaceUsedInPercent():0.###}%");
        foreach (var block in rom.Blocks) text.Append($" {block.Instruction.Mnemonic}={block.Base}+{block.Count}/{rom.FetchByteCodeFromMnemonic(block.Instruction.Mnemonic)}/{rom.OperandCount(block.Instruction.Mnemonic)}");
        using var digest = new Snapshot.Digest(0);
        for (int status = 0; status < DecoderRom.StatusVariants; status++)
        {
            foreach (var block in rom.Blocks)
            {
                for (int step = 0; step < block.Count; step++)
                {
                    var located = rom.Locate(status, block.Base + step);
                    var micro = rom.FetchInstruction(status, block.Base + step);
                    digest.Add($"{status}/{block.Base + step} {located?.Instruction.Mnemonic} {located?.Offset} {string.Join(",", micro.Select(m => $"{m.DeviceID}.{m.Function}"))}");
                }
            }
        }
        return text.Append('\n').Append(digest.Finish("  rom contents")).ToString();
    }

    private static string Diagnostics(string label, IEnumerable<MicrocodeDiagnostic> diagnostics)
    {
        var text = new StringBuilder($"-- {label}\n");
        foreach (var d in diagnostics) text.Append($"  {d.Severity} {d.Instruction} step {d.Step} {d.Signal}: {d.Message}\n");
        return text.ToString();
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Definition(string name)
    {
        var package = BuiltInPackages.Get(name);
        var text = new StringBuilder();
        text.Append(Errors("shipped", package.Machine));
        var mutations = new List<(string Label, Action<MachineDefinition> Change)>
        {
            ("no buses", d => d.Buses.Clear()),
            ("duplicate bus", d => d.Buses.Add(new BusDefinition { Id = d.Buses[0].Id })),
            ("empty bus id", d => d.Buses[0].Id = ""),
            ("duplicate device", d => d.Devices.Add(new DeviceDefinition { Id = d.Devices[0].Id, Type = d.Devices[0].Type })),
            ("unknown type", d => d.Devices.Add(new DeviceDefinition { Id = "zz", Type = "nosuch" })),
            ("missing type", d => d.Devices.Add(new DeviceDefinition { Id = "zz" })),
            ("bad id", d => d.Devices[0].Id = "has space"),
            ("unknown parameter", d => d.Devices[0].Parameters["nosuch"] = System.Text.Json.JsonDocument.Parse("1").RootElement),
            ("unknown port", d => d.Devices[0].SetPortBus("nosuchport", d.Buses[0].Id)),
            ("port to missing bus", d => { var p = d.Devices.SelectMany(x => x.Ports().Select(port => (x, port.Key))).FirstOrDefault(); if (p.x != null) p.x.SetPortBus(p.Key, "nosuchbus"); }),
            ("no decoder", d => d.Decoder = null),
            ("no status", d => d.Decoder.Status = null),
            ("status not a status", d => d.Decoder.Status = d.Buses[0].Id),
            ("no instruction register", d => d.Decoder.InstructionRegister = "nosuch"),
            ("interrupts wrong", d => d.Decoder.Interrupts = d.Devices[0].Id),
            ("halt missing", d => d.Halt = "nosuch"),
            ("program memory wrong", d => d.ProgramMemory = d.Devices[0].Id),
        };
        foreach (var device in package.Machine.Devices)
        {
            foreach (var connection in device.Connections.Keys.ToList())
            {
                mutations.Add(($"{device.Id}.{connection} missing", d => d.FindDevice(device.Id).Connections[connection] = "nosuch"));
                mutations.Add(($"{device.Id}.{connection} self", d => d.FindDevice(device.Id).Connections[connection] = device.Id));
            }
            mutations.Add(($"remove {device.Id}", d => d.Devices.RemoveAll(x => x.Id == device.Id)));
        }
        foreach (var (label, change) in mutations)
        {
            var copy = package.Machine.Clone();
            try
            {
                change(copy);
                text.Append(Errors(label, copy));
            }
            catch (Exception e)
            {
                text.Append($"-- {label}: threw {e.GetType().Name}: {e.Message}\n");
            }
        }
        Snapshot.Match($"definition-{name}", text.ToString());
    }

    private static string Errors(string label, MachineDefinition definition)
    {
        var text = new StringBuilder($"-- {label}\n");
        foreach (var error in Machine.ValidateDefinition(definition)) text.Append($"  {error}\n");
        try
        {
            var machine = new Machine(definition, "");
            text.Append($"  builds: {machine.Devices.Count} devices, {machine.MicrocodeWarnings.Count} microcode warnings\n");
        }
        catch (Exception e)
        {
            text.Append($"  build throws {e.GetType().Name}: {Flat(e.Message)}\n");
        }
        return text.ToString();
    }

    internal static string Flat(string text) => text?.Replace("\r", "").Replace("\n", "⏎");

    internal static string Hash(string text)
    {
        return text == null ? "null" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
