using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // What the assembler needs to know about the machine's instructions, by mnemonic as written. Each returns null
    // when it does not know or the instruction does not say.
    internal sealed record InstructionSet(Func<string, int?> Opcode, Func<string, int?> OperandCount, Func<string, int, OperandType?> OperandType);

    // What the passes of the assembler share.
    internal sealed class AssemblyContext
    {
        public AssemblyContext(AssemblyResult result, InstructionSet instructions, int memorySize, int registerCount)
        {
            Result = result;
            Instructions = instructions;
            MemorySize = memorySize;
            RegisterCount = registerCount;
            Diagnostics = new DiagnosticBag(result.Diagnostics);
        }

        public AssemblyResult Result { get; }
        public InstructionSet Instructions { get; }
        public int MemorySize { get; }
        public int RegisterCount { get; }
        public DiagnosticBag Diagnostics { get; }
        public List<Statement> Statements { get; } = new List<Statement>();
        public Dictionary<string, int> Labels { get; } = new Dictionary<string, int>();
        // Cells the whole program takes, known once the labels are declared.
        public int Size { get; set; }
        public List<int> Cells { get; } = new List<int>();
        public List<ListingLine> Listing { get; } = new List<ListingLine>();
    }

    // The assembler as passes over a shared context: statements from the parsed lines, then label addresses, then
    // cells, then the result.
    internal static class AssemblerPipeline
    {
        // Enough for the editor's hover and completion, which need the labels but no cells.
        public static readonly Action<AssemblyContext>[] Declaration = { Bind, DeclareLabels };
        public static readonly Action<AssemblyContext>[] Assembly = Declaration.Concat(new Action<AssemblyContext>[] { Emit, Finish }).ToArray();

        public static AssemblyContext Run(AssemblyContext context, IEnumerable<Action<AssemblyContext>> passes)
        {
            foreach (var pass in passes) pass(context);
            return context;
        }

        private static void Bind(AssemblyContext context)
        {
            context.Statements.AddRange(context.Result.Lines.Select(line => Statement.For(line, context.Instructions)));
        }

        // First pass: label addresses, after the problems found while reading each line.
        private static void DeclareLabels(AssemblyContext context)
        {
            foreach (var statement in context.Statements)
            {
                context.Diagnostics.AddRange(statement.Line.SyntaxErrors);
                Declare(context, statement.Line);
                context.Size += statement.CellCount();
            }
        }

        private static void Declare(AssemblyContext context, ParsedLine line)
        {
            if (line.Label == null) return;
            if (context.Labels.ContainsKey(line.Label.Name)) context.Diagnostics.Error(line, line.Label, $"label '{line.Label.Name}' is defined more than once");
            else context.Labels[line.Label.Name] = context.Size;
        }

        // Second pass: cells. Memory is checked line by line, so each line's problems stay together in the order
        // they are found.
        private static void Emit(AssemblyContext context)
        {
            foreach (var statement in context.Statements)
            {
                int start = context.Cells.Count;
                statement.Emit(context);
                CheckMemory(context, statement.Line, start);
                if (statement.IsListed) context.Listing.Add(statement.ToListing(start, context.Cells.Skip(start).ToArray()));
            }
        }

        // The line that runs past the end of program memory says so. Only lines with cells can.
        private static void CheckMemory(AssemblyContext context, ParsedLine line, int start)
        {
            if (context.Cells.Count > context.MemorySize && start <= context.MemorySize)
            {
                context.Diagnostics.Error(line, line.Mnemonic, $"the program is {context.Size} cells, but program memory only holds {context.MemorySize}");
            }
        }

        private static void Finish(AssemblyContext context)
        {
            context.Result.Cells = context.Cells.ToArray();
            context.Result.Listing = context.Listing;
            context.Result.Labels = new Dictionary<string, int>(context.Labels);
            context.Diagnostics.Sort();
        }
    }
}
