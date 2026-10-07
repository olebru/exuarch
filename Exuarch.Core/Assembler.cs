using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // Two pass assembler. Each opcode and operand takes one 16 bit memory cell; a string in .DATA or .STRING takes
    // one cell per character, and .STRING ends with a 0 cell. See AssemblyParser for the syntax.
    public class Assembler
    {
        public Dictionary<String, int> labelLUT;
        // One entry per source line that has a label or emits cells, in address order.
        public List<ListingLine> Listing { get; private set; } = new List<ListingLine>();
        public const string StringDirective = ".STRING";
        private readonly int memorySize;
        private readonly InstructionSet instructions;
        // Registers a register operand can name, R0 up to R(RegisterCount - 1): the machine's register file. 0 when
        // it has none.
        public int RegisterCount { get; set; }

        // Mnemonics are matched without regard to case.
        public Assembler(DecoderRom completeDecoderRom, int memorySize = MemoryModule.DefaultSize)
            : this(mnemonic => TryOpcode(completeDecoderRom, mnemonic),
                   mnemonic => completeDecoderRom.OperandCount(Canonical(completeDecoderRom.Microcode, mnemonic)), memorySize,
                   (mnemonic, index) => completeDecoderRom.Microcode.FindInstruction(Canonical(completeDecoderRom.Microcode, mnemonic))?.OperandTypeAt(index))
        {
        }

        // The instruction set's own spelling of a mnemonic, matched without regard to case.
        public static string Canonical(MicrocodeDefinition microcode, string mnemonic)
        {
            return microcode.Instructions.FirstOrDefault(i => i.Mnemonic == mnemonic)?.Mnemonic
                ?? microcode.Instructions.FirstOrDefault(i => string.Equals(i.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase))?.Mnemonic
                ?? mnemonic;
        }

        // opcodeOf returns null for an unknown mnemonic; operandCountOf and operandTypeOf return null when an
        // instruction does not say.
        public Assembler(Func<string, int?> opcodeOf, Func<string, int?> operandCountOf, int memorySize = MemoryModule.DefaultSize,
                         Func<string, int, OperandType?> operandTypeOf = null)
        {
            instructions = new InstructionSet(opcodeOf, operandCountOf, operandTypeOf ?? ((mnemonic, index) => null));
            this.memorySize = memorySize;
            labelLUT = new Dictionary<String, int>();
        }

        private static int? TryOpcode(DecoderRom rom, string mnemonic)
        {
            try { return rom.FetchByteCodeFromMnemonic(Canonical(rom.Microcode, mnemonic)); }
            catch (ArgumentException) { return null; }
        }

        // Assembles, throwing a FormatException for the first error. Warnings do not stop it.
        public int[] Assemble(string source)
        {
            var result = Analyze(source);
            var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (error != null) throw new FormatException(error.ToString());
            return result.Cells;
        }

        // Assembles as far as possible and reports every problem with its position; never throws.
        public AssemblyResult Analyze(string source)
        {
            var context = Run(source, AssemblerPipeline.Assembly);
            labelLUT = context.Labels;
            Listing = context.Listing;
            return context.Result;
        }

        // The program read as far as its labels, without looking up instructions or making cells: what the editor
        // needs for hover and completion.
        internal AssemblyContext Declare(string source)
        {
            return Run(source, AssemblerPipeline.Declaration);
        }

        private AssemblyContext Run(string source, Action<AssemblyContext>[] passes)
        {
            var result = new AssemblyResult { Lines = AssemblyParser.Parse(source) };
            return AssemblerPipeline.Run(new AssemblyContext(result, instructions, memorySize, RegisterCount), passes);
        }

        // R0, R1 ... in any case, below the number of registers there are.
        public static bool TryRegister(SourceToken operand, int count, out int register)
        {
            register = 0;
            var name = operand.Kind == TokenKind.LabelReference ? operand.Name : null;
            return name != null && name.Length >= 2 && (name[0] == 'R' || name[0] == 'r')
                && int.TryParse(name.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out register)
                && register < count;
        }
    }

    public class AssemblyResult
    {
        public List<ParsedLine> Lines { get; set; } = new List<ParsedLine>();
        public int[] Cells { get; set; } = Array.Empty<int>();
        public List<ListingLine> Listing { get; set; } = new List<ListingLine>();
        public Dictionary<string, int> Labels { get; set; } = new Dictionary<string, int>();
        public List<AssemblyDiagnostic> Diagnostics { get; } = new List<AssemblyDiagnostic>();
        // True when there are no errors; warnings are allowed.
        public bool Success { get { return !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error); } }
        public IEnumerable<AssemblyDiagnostic> Errors { get { return Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error); } }
        public IEnumerable<AssemblyDiagnostic> Warnings { get { return Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning); } }
    }

    public class ListingLine
    {
        public int LineNumber { get; set; }
        public string Text { get; set; }
        public string Label { get; set; }
        public string Mnemonic { get; set; }
        public string[] Operands { get; set; }
        public int Address { get; set; }
        public int[] Cells { get; set; }
        // False for .DATA / .STRING data and label only lines.
        public bool IsInstruction { get; set; }
    }
}
