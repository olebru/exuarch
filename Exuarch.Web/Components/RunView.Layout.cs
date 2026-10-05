using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Exuarch.Core;
using Microsoft.JSInterop;

namespace Exuarch.Web.Components
{
    // How the Run view is laid out, and the viewer's changes to it: the panels in the left and the right column and the
    // tabs below, which can be split in two side by side, in their order, and how big each is. Kept in this browser.
    public partial class RunView
    {
        public class Panel
        {
            public string Id;
            public string Title;
            // A second, quieter part of the title: which device a screen or LCD is.
            public string Detail;
            public string Css = "";
            // The body has its own scroll bar in the side column, rather than growing the column.
            public bool Scrolls;
            public (string Target, string Title)? Help;
        }

        private const string LayoutKey = "exuarch.runLayout";
        public const double DefaultLeftWidth = 340, DefaultRightWidth = 360, DefaultBottomHeight = 260, DefaultBottomSplit = 50;
        private static readonly string[] DockNames = { "left", "right", "bottom", "bottom2" };

        internal class SavedLayout
        {
            public List<string> Left { get; set; } = new List<string>();
            public List<string> Right { get; set; } = new List<string>();
            public List<string> Bottom { get; set; } = new List<string>();
            // The second group of tabs below, to the right of the first, when the panels below are split.
            public List<string> BottomRight { get; set; } = new List<string>();
            public bool BottomSplit { get; set; }
            // How much of the width below the first group takes, in percent.
            public double BottomSplitPercent { get; set; } = DefaultBottomSplit;
            public double LeftWidth { get; set; } = DefaultLeftWidth;
            public double RightWidth { get; set; } = DefaultRightWidth;
            public double BottomHeight { get; set; } = DefaultBottomHeight;
        }
        private SavedLayout runLayout = new SavedLayout();

        private double LeftWidth => runLayout.LeftWidth;
        private double RightWidth => runLayout.RightWidth;
        private double BottomHeight => runLayout.BottomHeight;
        private bool BottomSplit => runLayout.BottomSplit;

        // Every panel this machine has, in the default order: the side column's first, then the tabs'.
        private List<Panel> AllPanels
        {
            get
            {
                var panels = new List<Panel> { new Panel { Id = "clock", Title = "Clock speed", Css = "clock-panel" } };
                foreach (var screen in Machine.Devices.OfType<IScreen>()) panels.Add(new Panel { Id = "device:" + screen.ID(), Title = "Screen", Detail = screen.ID(), Css = "display-panel" });
                foreach (var display in Machine.Devices.OfType<CharacterDisplay>()) panels.Add(new Panel { Id = "device:" + display.ID(), Title = "LCD", Detail = display.ID(), Css = "display-panel" });
                foreach (var keypad in Machine.Devices.OfType<Keypad>()) panels.Add(new Panel { Id = "device:" + keypad.ID(), Title = "Keypad", Detail = keypad.ID(), Css = "display-panel" });
                panels.Add(new Panel { Id = "program", Title = "Program", Css = "program", Scrolls = true });
                panels.Add(new Panel { Id = "now", Title = "Now executing", Css = "now" });
                panels.Add(new Panel { Id = "memory", Title = "Memory", Css = "memory-panel", Scrolls = true, Help = ("memory-and-banks", "Memory and banks") });
                panels.Add(new Panel { Id = "decoder", Title = "Decoder ROM", Css = "decoder-panel", Scrolls = true, Help = ("flags-and-conditions", "The decoder ROM, flags and conditions") });
                panels.Add(new Panel { Id = "trace", Title = "Trace", Css = "trace-panel", Scrolls = true, Help = ("buses-and-ticks", "Buses and the two-phase tick") });
                return panels;
            }
        }
        // Where a panel starts out: the machine's input and output on the right, the memory, decoder ROM and trace below,
        // and how the program runs, the clock, the listing and what is executing, on the left.
        private static string DefaultDock(string id) => id.StartsWith("device:") ? "right" : id is "memory" or "decoder" or "trace" ? "bottom" : "left";

        private List<string> Saved(string dock) => dock switch { "left" => runLayout.Left, "right" => runLayout.Right, "bottom2" => runLayout.BottomRight, _ => runLayout.Bottom };

        // Where each panel is: where the viewer put it, and a panel they have never moved (a device new to this
        // machine, say) where it starts out.
        private Dictionary<string, List<Panel>> Docks()
        {
            var all = AllPanels;
            var byId = all.ToDictionary(p => p.Id);
            var docks = new Dictionary<string, List<Panel>>();
            var placed = new HashSet<Panel>();
            foreach (var dock in DockNames) docks[dock] = Saved(dock).Where(byId.ContainsKey).Select(id => byId[id]).Where(placed.Add).ToList();
            foreach (var panel in all.Where(p => !placed.Contains(p))) docks[DefaultDock(panel.Id)].Add(panel);
            // Not split, the second group's tabs are with the first.
            if (!runLayout.BottomSplit)
            {
                docks["bottom"].AddRange(docks["bottom2"]);
                docks["bottom2"].Clear();
            }
            return docks;
        }
        private List<Panel> Column(string dock) => Docks()[dock];
        // The tab on show in a group below: the one last chosen there, or its first.
        private Panel ActiveTab(List<Panel> group, string dock)
        {
            var chosen = dock == "bottom2" ? bottomTab2 : bottomTab;
            return group.FirstOrDefault(p => p.Id == chosen) ?? group.FirstOrDefault();
        }

        // Splits the panels below into two groups of tabs side by side, the last tab going to the new one, or puts them
        // back together.
        private async Task ToggleBottomSplit()
        {
            var docks = Docks();
            if (!runLayout.BottomSplit)
            {
                var first = docks["bottom"].Select(p => p.Id).ToList();
                runLayout.BottomRight = new List<string>();
                if (first.Count > 1)
                {
                    runLayout.BottomRight.Add(first[^1]);
                    first.RemoveAt(first.Count - 1);
                }
                runLayout.Bottom = first;
                bottomTab2 = runLayout.BottomRight.FirstOrDefault();
            }
            else
            {
                runLayout.Bottom = docks["bottom"].Concat(docks["bottom2"]).Select(p => p.Id).ToList();
                runLayout.BottomRight = new List<string>();
            }
            runLayout.BottomSplit = !runLayout.BottomSplit;
            if (Collapsed("bottom")) await TogglePanel("bottom");
            await SaveLayout();
        }

        // ---- Moving panels ----

        private string draggedPanel;
        // Where a dragged panel would land: in front of a panel, or last when the id is null.
        private (string Dock, string Before) dropBefore = (null, null);

        private void StartPanelDrag(string id) { draggedPanel = id; }
        private void EndPanelDrag() { draggedPanel = null; dropBefore = (null, null); }
        private void DragOver(string dock, string before)
        {
            if (draggedPanel != null && before != draggedPanel) dropBefore = (dock, before);
        }

        private async Task DropPanel(string dock, string before)
        {
            var id = draggedPanel;
            EndPanelDrag();
            if (id == null || id == before) return;
            var docks = Docks().ToDictionary(d => d.Key, d => d.Value.Select(p => p.Id).Where(p => p != id).ToList());
            var target = docks[dock];
            int index = before == null ? -1 : target.IndexOf(before);
            target.Insert(index < 0 ? target.Count : index, id);
            runLayout.Left = docks["left"];
            runLayout.Right = docks["right"];
            runLayout.Bottom = docks["bottom"];
            runLayout.BottomRight = docks["bottom2"];
            // A panel moved below opens as the tab on show, and a column it is dropped into opens if it was folded away.
            if (dock == "bottom") bottomTab = id;
            if (dock == "bottom2") bottomTab2 = id;
            var fold = dock == "bottom2" ? "bottom" : dock;
            if (Collapsed(fold)) await TogglePanel(fold);
            await SaveLayout();
        }

        private async Task ResetLayout()
        {
            runLayout = new SavedLayout();
            bottomTab = "memory";
            bottomTab2 = null;
            foreach (var dock in new[] { "left", "right", "bottom" }) if (Collapsed(dock)) await TogglePanel(dock);
            await SaveLayout();
        }

        // ---- Resizing: the handles are dragged in the browser, and only the size they end on comes back here ----

        private DotNetObjectReference<RunView> selfReference;

        [JSInvokable]
        public async Task LayoutResized(string kind, double size)
        {
            if (kind == "left") runLayout.LeftWidth = size < 0 ? DefaultLeftWidth : Math.Round(size);
            else if (kind == "right") runLayout.RightWidth = size < 0 ? DefaultRightWidth : Math.Round(size);
            else if (kind == "bottom") runLayout.BottomHeight = size < 0 ? DefaultBottomHeight : Math.Round(size);
            else if (kind == "split") runLayout.BottomSplitPercent = size < 0 ? DefaultBottomSplit : Math.Round(size, 1);
            // The drawing's panel changed width, and SchematicResized fits it again unless the viewer has zoomed.
            await SaveLayout();
        }

        private async Task LoadLayout()
        {
            try
            {
                var json = await JS.InvokeAsync<string>("exuarchStore.get", LayoutKey);
                if (!string.IsNullOrEmpty(json)) runLayout = JsonSerializer.Deserialize(json, RunLayoutJsonContext.Default.SavedLayout) ?? new SavedLayout();
            }
            catch (JsonException)
            {
                runLayout = new SavedLayout();
            }
            runLayout.Left ??= new List<string>();
            runLayout.Right ??= new List<string>();
            runLayout.Bottom ??= new List<string>();
            runLayout.BottomRight ??= new List<string>();
            if (runLayout.BottomSplitPercent is < 15 or > 85) runLayout.BottomSplitPercent = DefaultBottomSplit;
            if (runLayout.LeftWidth < 200) runLayout.LeftWidth = DefaultLeftWidth;
            if (runLayout.RightWidth < 200) runLayout.RightWidth = DefaultRightWidth;
            if (runLayout.BottomHeight < 80) runLayout.BottomHeight = DefaultBottomHeight;
        }

        private async Task SaveLayout()
        {
            await JS.InvokeAsync<bool>("exuarchStore.set", LayoutKey, JsonSerializer.Serialize(runLayout, RunLayoutJsonContext.Default.SavedLayout));
        }
    }

    // The runLayout as JSON without reflection, which the trimmed browser build does not keep.
    [JsonSerializable(typeof(RunView.SavedLayout))]
    internal partial class RunLayoutJsonContext : JsonSerializerContext
    {
    }
}
