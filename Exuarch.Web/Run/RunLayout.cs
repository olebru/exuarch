using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Web.Run
{
    // How the Run view is laid out, and the viewer's changes to it: which panels are in the left and the right column
    // and the tabs below, in their order, which tab is on show below, which panels are folded away, and a panel being
    // dragged to another place.
    public sealed class RunLayout
    {
        // What is folded away: "left", "right", "lines", "bottom", and any panel by its id: "clock", "now", "program",
        // "memory", "decoder", "trace" and "device:<id>" for a screen, LCD or keypad.
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private string bottomTab = "memory";
        // The tab on show in the second group below, when the panels below are split.
        private string bottomTab2;
        // Where a dragged panel would land: in front of a panel, or last when the id is null.
        private (Dock? Dock, string Before) dropBefore = (null, null);

        public SavedLayout Saved { get; private set; } = new SavedLayout();
        // The panel being dragged, by id.
        public string Dragged { get; private set; }

        // The layout this browser kept, or with null the one there is, made whole.
        public void Load(SavedLayout saved)
        {
            Saved = (saved ?? Saved).Normalize();
        }

        // ---- Folding ----

        public bool Collapsed(string key) => collapsed.Contains(key);

        public void Toggle(string key)
        {
            if (!collapsed.Remove(key)) collapsed.Add(key);
        }

        // What is folded, as this browser keeps it.
        public string CollapsedList => string.Join(",", collapsed.OrderBy(key => key));

        // Folds what this browser kept as folded: false when it kept nothing.
        public bool FoldSaved(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return false;
            collapsed.UnionWith(saved.Split(',', StringSplitOptions.RemoveEmptyEntries));
            return true;
        }

        // ---- Where each panel is ----

        // Where the viewer put each panel, and a panel they have never moved (a device new to this machine, say) where
        // it starts out. Not split, the second group's tabs are with the first.
        public Dictionary<Dock, List<RunPanel>> Place(IReadOnlyList<RunPanel> panels)
        {
            var byId = panels.ToDictionary(p => p.Id);
            var placed = new HashSet<RunPanel>();
            var docks = Enum.GetValues<Dock>().ToDictionary(dock => dock, dock => Saved.In(dock).Where(byId.ContainsKey).Select(id => byId[id]).Where(placed.Add).ToList());
            foreach (var panel in panels.Where(p => !placed.Contains(p))) docks[panel.DefaultDock].Add(panel);
            if (!Saved.BottomSplit)
            {
                docks[Dock.Bottom].AddRange(docks[Dock.Bottom2]);
                docks[Dock.Bottom2].Clear();
            }
            return docks;
        }

        // The tab on show in a group below: the one last chosen there, or its first.
        public RunPanel ActiveTab(Dock dock, List<RunPanel> group)
        {
            var chosen = dock == Dock.Bottom2 ? bottomTab2 : bottomTab;
            return group.FirstOrDefault(p => p.Id == chosen) ?? group.FirstOrDefault();
        }

        public void ShowTab(Dock dock, string id)
        {
            if (dock == Dock.Bottom2) bottomTab2 = id;
            else bottomTab = id;
        }

        // Splits the panels below into two groups of tabs side by side, the last tab going to the new one, or puts them
        // back together.
        public void ToggleSplit(Dictionary<Dock, List<RunPanel>> docks)
        {
            if (Saved.BottomSplit) Join(docks);
            else Split(docks);
            Saved.BottomSplit = !Saved.BottomSplit;
        }

        private void Split(Dictionary<Dock, List<RunPanel>> docks)
        {
            var first = docks[Dock.Bottom].Select(p => p.Id).ToList();
            Saved.BottomRight = new List<string>();
            if (first.Count > 1)
            {
                Saved.BottomRight.Add(first[^1]);
                first.RemoveAt(first.Count - 1);
            }
            Saved.Bottom = first;
            bottomTab2 = Saved.BottomRight.FirstOrDefault();
        }

        private void Join(Dictionary<Dock, List<RunPanel>> docks)
        {
            Saved.Bottom = docks[Dock.Bottom].Concat(docks[Dock.Bottom2]).Select(p => p.Id).ToList();
            Saved.BottomRight = new List<string>();
        }

        // Everything where it started, at its first size, and every dock open.
        public IReadOnlyList<string> Reset()
        {
            Saved = new SavedLayout();
            bottomTab = "memory";
            bottomTab2 = null;
            return new[] { Dock.Left, Dock.Right, Dock.Bottom }.Select(dock => dock.Key()).Where(Collapsed).ToList();
        }

        // ---- Moving panels ----

        public void StartDrag(string id)
        {
            Dragged = id;
        }

        public void EndDrag()
        {
            Dragged = null;
            dropBefore = (null, null);
        }

        public void DragOver(Place place)
        {
            if (Dragged != null && place.PanelId != Dragged) dropBefore = (place.Dock, place.PanelId);
        }

        // Where the dragged panel would land: in front of the panel, or last in the dock for null.
        public bool DropsBefore(Dock dock, string id) => dropBefore == (dock, id);

        // Puts the dragged panel in front of a panel, or last for null: false when nothing moved. A panel moved below
        // opens as the tab on show.
        public bool Drop(Place place, Dictionary<Dock, List<RunPanel>> docks)
        {
            var id = Dragged;
            EndDrag();
            if (id == null || id == place.PanelId) return false;
            var ids = docks.ToDictionary(d => d.Key, d => d.Value.Select(p => p.Id).Where(p => p != id).ToList());
            var target = ids[place.Dock];
            int index = place.PanelId == null ? -1 : target.IndexOf(place.PanelId);
            target.Insert(index < 0 ? target.Count : index, id);
            (Saved.Left, Saved.Right, Saved.Bottom, Saved.BottomRight) = (ids[Dock.Left], ids[Dock.Right], ids[Dock.Bottom], ids[Dock.Bottom2]);
            if (place.Dock is Dock.Bottom or Dock.Bottom2) ShowTab(place.Dock, id);
            return true;
        }
    }

    // A place among the panels: in a dock, at a panel (to show it, or to drop another in front of it), or for null at
    // the end of the dock.
    public readonly record struct Place(Dock Dock, string PanelId);
}
