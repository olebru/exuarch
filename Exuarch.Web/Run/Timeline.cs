using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    public sealed class Timeline
    {
        private static readonly Dictionary<string, string> Marks = new Dictionary<string, string> { ["ran"] = "●", ["next"] = "▶", ["done"] = "✓" };

        public static Timeline Of(RunSession session)
        {
            var machine = session.Machine;
            var last = machine.LastTick;
            var next = machine.NextStep;
            return last?.Instruction != null
                ? new Timeline(session.InstructionNamed(last.Instruction), last.Status, last.StepIndex, next)
                : new Timeline(next?.Instruction, machine.NextDecoderStatus, null, next);
        }

        private Timeline(InstructionDefinition instruction, int status, int? ranIndex, (InstructionDefinition Instruction, MicroStep Step, int Offset)? next)
        {
            Instruction = instruction;
            if (instruction == null) return;
            var variant = instruction.StepsFor(status);
            int ranAt = ranIndex.HasValue ? variant.FindIndex(v => instruction.Steps.IndexOf(v) == ranIndex) : -1;
            Steps = variant.Select((step, i) => StepAt(i, step, instruction.Steps.IndexOf(step) == ranIndex, IsNext(next, instruction, step), ranAt)).ToList();
            if (next != null && next.Value.Instruction != instruction) UpNext = (next.Value.Instruction.Mnemonic, next.Value.Offset + 1);
        }

        public InstructionDefinition Instruction { get; }
        public bool Conditional => Instruction.Steps.Any(s => s.When != null);
        public IReadOnlyList<TimelineStep> Steps { get; } = new List<TimelineStep>();
        public (string Mnemonic, int Step)? UpNext { get; }

        private static bool IsNext((InstructionDefinition Instruction, MicroStep Step, int Offset)? next, InstructionDefinition instruction, MicroStep step)
        {
            return next != null && next.Value.Instruction == instruction && next.Value.Step == step;
        }

        private static TimelineStep StepAt(int i, MicroStep step, bool ran, bool isNext, int ranAt)
        {
            var state = ran ? "ran" : isNext ? "next" : i < ranAt ? "done" : "todo";
            return new TimelineStep(state, Marks.TryGetValue(state, out var mark) ? mark : (i + 1).ToString(), step.Signals);
        }
    }

    public sealed record TimelineStep(string State, string Mark, IReadOnlyList<string> Signals);
}
