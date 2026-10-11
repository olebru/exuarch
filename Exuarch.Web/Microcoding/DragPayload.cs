using System.Threading.Tasks;

namespace Exuarch.Web.Microcoding
{
    public abstract record DragPayload
    {
        public virtual Task DropOnInstruction(MicrocodeSession session, int index) => Task.CompletedTask;
        public virtual Task DropOnStep(MicrocodeSession session, int step) => Task.CompletedTask;
        public virtual Task DropOnNewStep(MicrocodeSession session) => Task.CompletedTask;
    }

    public sealed record InstructionDrag(int Index) : DragPayload
    {
        public override Task DropOnInstruction(MicrocodeSession session, int index)
        {
            return index == Index ? Task.CompletedTask : session.MoveInstruction(Index, index);
        }
    }

    public sealed record StepDrag(int Step) : DragPayload
    {
        public override Task DropOnStep(MicrocodeSession session, int step) => session.MoveStep(Step, step);
    }

    public abstract record SignalPayload(string Signal) : DragPayload
    {
        public override async Task DropOnNewStep(MicrocodeSession session)
        {
            await session.AddStep();
            await DropOnStep(session, session.Selected.Steps.Count - 1);
        }
    }

    public sealed record SignalDrag(int Step, string Signal) : SignalPayload(Signal)
    {
        public override Task DropOnStep(MicrocodeSession session, int step)
        {
            return step == Step ? Task.CompletedTask : session.MoveSignal(Signal, Step, step);
        }
    }

    public sealed record PaletteSignalDrag(string Signal) : SignalPayload(Signal)
    {
        public override Task DropOnStep(MicrocodeSession session, int step) => session.AddSignal(step, Signal);
    }
}
