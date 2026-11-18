using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Exuarch.Web.Run
{
    public enum Dock { Left, Right, Bottom, Bottom2, InnerLeft, InnerRight }

    public static class DockNames
    {
        public static string Key(this Dock dock) => dock.ToString().ToLowerInvariant();

        public static string Fold(this Dock dock) => dock == Dock.Bottom2 ? Dock.Bottom.Key() : dock.Key();
    }

    public sealed class SavedLayout
    {
        public const double DefaultLeftWidth = 340, DefaultInnerWidth = 340, DefaultRightWidth = 360, DefaultBottomHeight = 260, DefaultBottomSplit = 50;

        public List<string> Left { get; set; } = new List<string>();
        public List<string> Right { get; set; } = new List<string>();
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> InnerLeft { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> InnerRight { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> Shown { get; set; }
        public List<string> Bottom { get; set; } = new List<string>();
        public List<string> BottomRight { get; set; } = new List<string>();
        public bool BottomSplit { get; set; }
        public double BottomSplitPercent { get; set; } = DefaultBottomSplit;
        public double LeftWidth { get; set; } = DefaultLeftWidth;
        public double RightWidth { get; set; } = DefaultRightWidth;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? InnerLeftWidth { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? InnerRightWidth { get; set; }
        public double BottomHeight { get; set; } = DefaultBottomHeight;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, double> PanelHeights { get; set; }

        public const string PanelPrefix = "panel:";
        public const double MinPanelHeight = 60;

        private sealed record Handle(Func<SavedLayout, double> Get, Action<SavedLayout, double> Set, double Min, double Max, double Default, int Decimals);

        private static readonly Dictionary<string, Handle> Handles = new Dictionary<string, Handle>
        {
            ["left"] = new Handle(l => l.LeftWidth, (l, size) => l.LeftWidth = size, 200, double.MaxValue, DefaultLeftWidth, 0),
            ["innerleft"] = new Handle(l => l.InnerLeftWidth ?? DefaultInnerWidth, (l, size) => l.InnerLeftWidth = NullWhenDefault(size), 200, double.MaxValue, DefaultInnerWidth, 0),
            ["innerright"] = new Handle(l => l.InnerRightWidth ?? DefaultInnerWidth, (l, size) => l.InnerRightWidth = NullWhenDefault(size), 200, double.MaxValue, DefaultInnerWidth, 0),
            ["right"] = new Handle(l => l.RightWidth, (l, size) => l.RightWidth = size, 200, double.MaxValue, DefaultRightWidth, 0),
            ["bottom"] = new Handle(l => l.BottomHeight, (l, size) => l.BottomHeight = size, 80, double.MaxValue, DefaultBottomHeight, 0),
            ["split"] = new Handle(l => l.BottomSplitPercent, (l, size) => l.BottomSplitPercent = size, 15, 85, DefaultBottomSplit, 1),
        };

        public List<string> In(Dock dock) => dock switch
        {
            Dock.Left => Left,
            Dock.Right => Right,
            Dock.InnerLeft => InnerLeft ?? new List<string>(),
            Dock.InnerRight => InnerRight ?? new List<string>(),
            Dock.Bottom2 => BottomRight,
            _ => Bottom,
        };

        private static double? NullWhenDefault(double size) => size == DefaultInnerWidth ? null : size;

        public bool Shows(string key) => Shown?.Contains(key) == true;

        public void ToggleShown(string key)
        {
            Shown ??= new List<string>();
            if (!Shown.Remove(key)) Shown.Add(key);
            if (Shown.Count == 0) Shown = null;
        }

        public void KeepInner(Dock dock, List<string> ids)
        {
            var kept = ids.Count == 0 ? null : ids;
            if (dock == Dock.InnerLeft) InnerLeft = kept;
            else InnerRight = kept;
        }

        public double? PanelHeight(string id) => PanelHeights != null && PanelHeights.TryGetValue(id, out var height) ? height : null;

        public void Resize(string kind, double size)
        {
            if (kind.StartsWith(PanelPrefix, StringComparison.Ordinal))
            {
                ResizePanel(kind.Substring(PanelPrefix.Length), size);
                return;
            }
            if (!Handles.TryGetValue(kind, out var handle)) return;
            bool restoresDefault = size < 0;
            handle.Set(this, restoresDefault ? handle.Default : Math.Round(size, handle.Decimals));
        }

        private void ResizePanel(string id, double size)
        {
            if (size >= MinPanelHeight) (PanelHeights ??= new Dictionary<string, double>())[id] = Math.Round(size);
            else PanelHeights?.Remove(id);
            if (PanelHeights?.Count == 0) PanelHeights = null;
        }

        private void DropTooSmallPanelHeights()
        {
            if (PanelHeights == null) return;
            foreach (var id in PanelHeights.Where(entry => entry.Value < MinPanelHeight).Select(entry => entry.Key).ToList()) ResizePanel(id, -1);
        }

        public SavedLayout Normalize()
        {
            DropTooSmallPanelHeights();
            Left ??= new List<string>();
            Right ??= new List<string>();
            Bottom ??= new List<string>();
            BottomRight ??= new List<string>();
            foreach (var handle in Handles.Values)
            {
                double size = handle.Get(this);
                if (size < handle.Min || size > handle.Max) handle.Set(this, handle.Default);
            }
            return this;
        }
    }

    [JsonSerializable(typeof(SavedLayout))]
    internal partial class RunLayoutJsonContext : JsonSerializerContext
    {
    }
}
