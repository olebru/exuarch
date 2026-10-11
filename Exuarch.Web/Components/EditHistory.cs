using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Exuarch.Web.Components
{
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

        public void Record()
        {
            Push(snapshot());
        }
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
