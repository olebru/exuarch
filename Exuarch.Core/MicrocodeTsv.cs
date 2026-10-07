using System;
using System.Linq;
namespace Exuarch.Core
{
    // One row of the legacy tab separated microcode: clock flag, device, function, mnemonic, then N, V, C and Z.
    internal readonly record struct TsvRow(string Text, int LineNumber, bool StartsStep, string Device, string Function, string Mnemonic, string Status)
    {
        public static TsvRow Parse(string line, int lineNumber)
        {
            var tokens = line.Split('\t');
            if (tokens.Length < 8)
            {
                throw new FormatException($"Decoder ROM line {lineNumber}: expected 8 tab separated columns, found {tokens.Length}: '{line}'");
            }
            var status = string.Concat(tokens.Skip(4).Take(4));
            if (status.Length != 4 || status.Any(c => c != '0' && c != '1' && c != 'x'))
            {
                throw new FormatException($"Decoder ROM line {lineNumber}: status columns must each be 0, 1 or x: '{line}'");
            }
            var clock = tokens[0];
            if (clock != "p" && clock != "s")
            {
                throw new FormatException($"Decoder ROM line {lineNumber}: clock flag must be 'p' (new step) or 's' (same step): '{line}'");
            }
            return new TsvRow(line, lineNumber, clock == "p", tokens[1], tokens[2], tokens[3], status);
        }
    }

    // Builds one instruction from its rows. A "p" row starts a new micro step and "s" rows add to the current one;
    // a step's first row starts it whatever its flag, so a "p" only starts another once the step has had one.
    internal sealed class TsvInstruction
    {
        private bool stepHasClock;

        public TsvInstruction(string mnemonic)
        {
            Definition = new InstructionDefinition { Mnemonic = mnemonic };
        }
        public InstructionDefinition Definition { get; }

        // Rows of one step must share the same flag condition.
        public void Add(TsvRow row)
        {
            var condition = FlagCondition.FromPattern(row.Status);
            var steps = Definition.Steps;
            if (steps.Count == 0 || (row.StartsStep && stepHasClock))
            {
                steps.Add(new MicroStep { When = condition });
                stepHasClock = row.StartsStep;
            }
            else if (!FlagCondition.AreEqual(steps[^1].When, condition))
            {
                throw new FormatException($"Decoder ROM line {row.LineNumber}: flag condition {row.Status} differs from the step it belongs to ({FlagCondition.ToPattern(steps[^1].When)}); start a new step with 'p': '{row.Text}'");
            }
            else
            {
                stepHasClock |= row.StartsStep;
            }
            steps[^1].Signals.Add($"{row.Device}.{row.Function}");
        }
    }
}
