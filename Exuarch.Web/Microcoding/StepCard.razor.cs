using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Microcoding
{
    // One micro step of the picked instruction, like a line in the decoder ROM: the flags it runs on, its signals, the
    // bus transfers they make, its comment and its problems. The step being edited has a field to add a signal.
    public partial class StepCard
    {
        [Parameter] public MicrocodeSession Session { get; set; }
        [Parameter] public MicroStep Step { get; set; }
        [Parameter] public int Index { get; set; }

        private KeyedHandlers<string, MouseEventArgs> cycleFlag;

        private bool Active => Session.ActiveStep == Index;
        private bool IsLast => Index == Session.Selected.Steps.Count - 1;
        private List<MicrocodeDiagnostic> Diagnostics => Session.DiagnosticsFor(Session.Selected, Index).ToList();

        // Edited, dimmed when the preview's flags skip it, and marked with its worst problem.
        private string CssClass
        {
            get
            {
                var worst = DiagnosticStyle.Worst(Diagnostics);
                return $"step {(Active ? "active" : "")} {(Session.Runs(Step) ? "" : "dimmed")} {(worst == "" ? "" : "has-" + worst)}";
            }
        }

        protected override void OnInitialized()
        {
            cycleFlag = new KeyedHandlers<string, MouseEventArgs>((flag, _) => Session.CycleFlag(Step, flag));
        }

        private async Task OnSignalKey(KeyboardEventArgs e)
        {
            if (e.Key == "Enter") await Session.AddNewSignal(Index);
        }
    }
}
