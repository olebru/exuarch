using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace Exuarch.Core
{
    // Editor support for assembly source, driven by a machine's microcode: diagnostics, completion, hover and
    // formatting. Lines and columns are 1 based.
    public class AssemblyLanguage
    {
        private readonly MicrocodeDefinition microcode;
        private readonly Assembler assembler;
        private readonly AssemblyHover hover;
        private readonly AssemblyCompletion completion;
        // The source last read for hover and completion, which the editor asks about many times between edits.
        private SemanticModel model;

        // registerCount: the registers of the machine's register file, which register operands can name (see
        // RegisterFile.CountIn); 0 when it has none.
        public AssemblyLanguage(MicrocodeDefinition microcode, int memorySize = MemoryModule.DefaultSize, int registerCount = 0)
        {
            RegisterCount = registerCount;
            this.microcode = microcode ?? new MicrocodeDefinition();
            DecoderRom rom;
            try { rom = new DecoderRom(this.microcode); }
            catch (Exception) { rom = null; }
            assembler = rom != null
                ? new Assembler(rom, memorySize)
                : new Assembler(m => Instruction(m) != null ? 0 : null, m => Instruction(m)?.OperandCount, memorySize, (m, i) => Instruction(m)?.OperandTypeAt(i));
            assembler.RegisterCount = registerCount;
            hover = new AssemblyHover(rom, registerCount);
            completion = new AssemblyCompletion(Instructions, registerCount);
        }

        public int RegisterCount { get; }

        // Mnemonics a program can use: every instruction except the fetch routine.
        public IEnumerable<InstructionDefinition> Instructions
        {
            get { return microcode.Instructions.Where(i => !string.IsNullOrWhiteSpace(i.Mnemonic)); }
        }
        private InstructionDefinition Instruction(string mnemonic)
        {
            var canonical = Assembler.Canonical(microcode, mnemonic);
            return Instructions.FirstOrDefault(i => i.Mnemonic == canonical);
        }

        public AssemblyResult Analyze(string source)
        {
            return assembler.Analyze(source);
        }

        // Lines and labels depend on the source alone, so they are read again only when it changes.
        private SemanticModel Model(string source)
        {
            var last = model;
            if (last != null && last.Source == source) return last;
            return model = new SemanticModel(source, assembler.Declare(source), Instruction);
        }

        public List<CompletionItem> Complete(string source, int lineNumber, int column)
        {
            return completion.Complete(Model(source), lineNumber, column);
        }

        // Ends a hover with a link to the handbook page on assembly, opened in the app by the editor.
        public const string ReadMore = "\n\n[Read more: Assembly](exuarch:guide/assembly)";

        // Markdown describing the token under the position, or null.
        public string Hover(string source, int lineNumber, int column)
        {
            return hover.Describe(Model(source).SymbolAt(lineNumber, column));
        }

        // ---- Formatting ----

        // Lines up labels, mnemonics, operands and comments in columns. Lines with syntax errors are left as they are.
        public static string Format(string source)
        {
            var lines = AssemblyParser.Parse(source);
            var layout = Layout.For(lines);
            return string.Join("\n", lines.Select(l => FormatLine(l, layout)));
        }

        // One line formatted with the column widths of the whole document, for format on type.
        public static string FormatLine(string source, int lineNumber)
        {
            var lines = AssemblyParser.Parse(source);
            var line = lines.ElementAtOrDefault(lineNumber - 1);
            return line == null ? "" : FormatLine(line, Layout.For(lines));
        }

        // As Format, and also writes each mnemonic the way the instruction set spells it (lai becomes LAI).
        public string FormatDocument(string source)
        {
            var lines = WithCanonicalMnemonics(source);
            var layout = Layout.For(lines);
            return string.Join("\n", lines.Select(l => FormatLine(l, layout)));
        }
        public string FormatDocumentLine(string source, int lineNumber)
        {
            var lines = WithCanonicalMnemonics(source);
            var line = lines.ElementAtOrDefault(lineNumber - 1);
            return line == null ? "" : FormatLine(line, Layout.For(lines));
        }
        private List<ParsedLine> WithCanonicalMnemonics(string source)
        {
            var lines = AssemblyParser.Parse(source);
            foreach (var line in lines.Where(l => l.Mnemonic?.Kind == TokenKind.Mnemonic))
            {
                line.Mnemonic.Text = Assembler.Canonical(microcode, line.Mnemonic.Text);
            }
            foreach (var line in lines.Where(l => l.Mnemonic?.Kind == TokenKind.Directive))
            {
                line.Mnemonic.Text = line.Mnemonic.Text.ToUpperInvariant();
            }
            return lines;
        }

        private class Layout
        {
            public int MnemonicColumn;
            public int OperandColumn;
            public int CommentColumn;

            public static Layout For(List<ParsedLine> lines)
            {
                var clean = lines.Where(l => l.SyntaxErrors.Count == 0).ToList();
                int labelWidth = clean.Where(l => l.Label != null).Select(l => l.Label.Text.Length + 1).DefaultIfEmpty(0).Max();
                int mnemonicColumn = Math.Max(8, labelWidth);
                int mnemonicWidth = Math.Max(4, clean.Where(l => l.Mnemonic != null).Select(l => l.Mnemonic.Text.Length).DefaultIfEmpty(0).Max()) + 2;
                var layout = new Layout { MnemonicColumn = mnemonicColumn, OperandColumn = mnemonicColumn + mnemonicWidth };
                int codeWidth = clean.Where(l => l.Comment != null && (l.Mnemonic != null || l.Label != null))
                                     .Select(l => Code(l, layout).Length).DefaultIfEmpty(0).Max();
                layout.CommentColumn = Math.Min(48, Math.Max(codeWidth + 2, layout.OperandColumn + 12));
                return layout;
            }
        }

        private static string Code(ParsedLine line, Layout layout)
        {
            var text = new StringBuilder();
            if (line.Label != null) text.Append(line.Label.Text);
            if (line.Mnemonic != null)
            {
                text.Append(' ', Math.Max(1, layout.MnemonicColumn - text.Length));
                text.Append(line.Mnemonic.Text);
                if (line.Operands.Count > 0)
                {
                    text.Append(' ', Math.Max(1, layout.OperandColumn - text.Length));
                    text.Append(string.Join(", ", line.Operands.Select(OperandText)));
                }
            }
            return text.ToString();
        }

        // Literals are written without the older # prefix.
        private static string OperandText(SourceToken operand)
        {
            return operand.Kind == TokenKind.Number || operand.Kind == TokenKind.Character ? operand.Text.TrimStart('#') : operand.Text;
        }

        private static string FormatLine(ParsedLine line, Layout layout)
        {
            if (line.SyntaxErrors.Count > 0) return line.Text;
            var code = Code(line, layout);
            if (line.Comment == null) return code;
            var comment = "; " + line.Comment.Text.Substring(1).Trim();
            if (code.Length == 0)
            {
                // A comment on its own line keeps to the left edge, or lines up with the code if it was indented.
                bool indented = line.Text.Length > 0 && char.IsWhiteSpace(line.Text[0]);
                return (indented ? new string(' ', layout.MnemonicColumn) : "") + comment;
            }
            return code + new string(' ', Math.Max(1, layout.CommentColumn - code.Length)) + comment;
        }
    }

    public enum CompletionKind { Instruction, Directive, Label }

    public class CompletionItem
    {
        public string Label { get; set; }
        public CompletionKind Kind { get; set; }
        public string Detail { get; set; }
        public string Documentation { get; set; }
        public string InsertText { get; set; }
    }
}
