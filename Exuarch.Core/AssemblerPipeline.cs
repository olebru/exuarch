using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    internal sealed record InstructionSet(Func<string, int?> Opcode, Func<string, int?> OperandCount, Func<string, int, OperandType?> OperandType, Func<string, int, FieldSlot?> Field)
    {
        public int FieldOperands(string mnemonic, int written)
        {
            int count = 0;
            for (int operand = 0; operand < written; operand++) if (Field(mnemonic, operand) != null) count++;
            return count;
        }
    }

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
        public int Size { get; set; }
        public List<int> Cells { get; } = new List<int>();
        public List<ListingLine> Listing { get; } = new List<ListingLine>();
    }

    internal static class AssemblerPipeline
    {
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
