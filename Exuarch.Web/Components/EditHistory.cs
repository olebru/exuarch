using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Exuarch.Web.Components
{
    // One undo/redo history for the machine definition, microcode included, shared by the editors. Snapshots are the
    // definition as JSON; the page supplies how to take and restore one. It covers one machine: opening another
    // clears it, because a snapshot of one machine's hardware must never be restored into another package.
    public sealed class EditHistory
    {
        private const int Limit = 200;
        private readonly LinkedList<string> undo = new LinkedList<string>();
        private readonly Stack<string> redo = new Stack<string>();
        private readonly Func<string> snapshot;
        private readonly Func<string, Task> restore;

        public EditHistory(Func<string> snapshot, Func<string, Task> restore)
        {
            this.snapshot = snapshot;
            this.restore = restore;
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        // Call before changing the definition.
        public void Record()
        {
            Push(snapshot());
        }
        // Records a snapshot taken earlier, for example at the start of a drag.
        public void Push(string earlier)
        {
            undo.AddLast(earlier);
            if (undo.Count > Limit) undo.RemoveFirst();
            redo.Clear();
        }
        public void Clear()
        {
            undo.Clear();
            redo.Clear();
        }
        // Drops the last record, when the change it was taken for did not happen.
        public void Discard()
        {
            if (undo.Count > 0) undo.RemoveLast();
        }
        public async Task Undo()
        {
            if (!CanUndo) return;
            redo.Push(snapshot());
            var previous = undo.Last.Value;
            undo.RemoveLast();
            await restore(previous);
        }
        public async Task Redo()
        {
            if (!CanRedo) return;
            undo.AddLast(snapshot());
            await restore(redo.Pop());
        }
    }
}
