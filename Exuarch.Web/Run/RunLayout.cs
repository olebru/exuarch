using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Web.Run
{
    public sealed class RunLayout
    {
        private const string DefaultBottomTab = "memory";

        private readonly HashSet<string> collapsed = new HashSet<string>();
        private string bottomTab = DefaultBottomTab;
        private string bottomTab2;
        private (Dock? Dock, string Before) dropBefore = (null, null);

        public SavedLayout Saved { get; private set; } = new SavedLayout();
        public string Dragged { get; private set; }

        public void Load(SavedLayout saved)
        {
            Saved = (saved ?? Saved).Normalize();
        }

        public bool Collapsed(string key) => collapsed.Contains(key);

        public void Toggle(string key)
        {
            if (!collapsed.Remove(key)) collapsed.Add(key);
        }

        public string CollapsedList => string.Join(",", collapsed.OrderBy(key => key));

        public bool FoldSaved(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return false;
            collapsed.UnionWith(saved.Split(',', StringSplitOptions.RemoveEmptyEntries));
            return true;
        }

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

        public IReadOnlyList<string> Reset()
        {
            Saved = new SavedLayout();
            bottomTab = DefaultBottomTab;
            bottomTab2 = null;
            return new[] { Dock.Left, Dock.Right, Dock.Bottom }.Select(dock => dock.Key()).Where(Collapsed).ToList();
        }

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

        public bool DropsBefore(Dock dock, string id) => dropBefore == (dock, id);

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

    public readonly record struct Place(Dock Dock, string PanelId);
}
