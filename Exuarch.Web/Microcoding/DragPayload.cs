using System.Threading.Tasks;

namespace Exuarch.Web.Microcoding
{
    // What is being dragged in the microcode editor, and what dropping it on each place does: on an instruction in the
    // list, on a step, or on "Add step". Dropping it where it does not belong does nothing.
    public abstract record DragPayload
    {
        public virtual Task DropOnInstruction(MicrocodeSession session, int index) => Task.CompletedTask;
        public virtual Task DropOnStep(MicrocodeSession session, int step) => Task.CompletedTask;
        public virtual Task DropOnNewStep(MicrocodeSession session) => Task.CompletedTask;
    }

    // An instruction in the list, moved to where it is dropped.
    public sealed record InstructionDrag(int Index) : DragPayload
    {
        public override Task DropOnInstruction(MicrocodeSession session, int index)
        {
            return index == Index ? Task.CompletedTask : session.MoveInstruction(Index, index);
        }
    }

    // A step by its handle, moved to where it is dropped.
    public sealed record StepDrag(int Step) : DragPayload
    {
        public override Task DropOnStep(MicrocodeSession session, int step) => session.MoveStep(Step, step);
    }

    // A signal, from a step or from the palette; dropped on "Add step" it goes into a new step.
    public abstract record SignalPayload(string Signal) : DragPayload
    {
        public override async Task DropOnNewStep(MicrocodeSession session)
        {
            await session.AddStep();
            await DropOnStep(session, session.Selected.Steps.Count - 1);
        }
    }

    // A signal in a step, moved to another step.
    public sealed record SignalDrag(int Step, string Signal) : SignalPayload(Signal)
    {
        public override Task DropOnStep(MicrocodeSession session, int step)
        {
            return step == Step ? Task.CompletedTask : session.MoveSignal(Signal, Step, step);
        }
    }

    // A control line from the palette, added to the step.
    public sealed record PaletteSignalDrag(string Signal) : SignalPayload(Signal)
    {
        public override Task DropOnStep(MicrocodeSession session, int step) => session.AddSignal(step, Signal);
    }
}
