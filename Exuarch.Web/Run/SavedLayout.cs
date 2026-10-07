using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Exuarch.Web.Run
{
    // Where the panels of the Run view go: a column on either side of the drawing, and tabs below it, which can be split
    // into two groups side by side.
    public enum Dock { Left, Right, Bottom, Bottom2 }

    public static class DockNames
    {
        // How a dock is named in the markup and among the folded panels: "left", "right", "bottom" and "bottom2".
        public static string Key(this Dock dock) => dock.ToString().ToLowerInvariant();

        // What folds the dock away: the second group of tabs below folds with the first.
        public static string Fold(this Dock dock) => dock == Dock.Bottom2 ? Dock.Bottom.Key() : dock.Key();
    }

    // The viewer's layout of the Run view as this browser keeps it, as JSON: the panels in each dock in their order, and
    // how big the columns and the panels below are.
    public sealed class SavedLayout
    {
        public const double DefaultLeftWidth = 340, DefaultRightWidth = 360, DefaultBottomHeight = 260, DefaultBottomSplit = 50;

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

        // Each resize handle (data-resize in the markup): the size it sets, the range a kept size must be in, the size a
        // double-click puts back, and the decimals it is kept to.
        private sealed record Handle(Func<SavedLayout, double> Get, Action<SavedLayout, double> Set, double Min, double Max, double Default, int Decimals);

        private static readonly Dictionary<string, Handle> Handles = new Dictionary<string, Handle>
        {
            ["left"] = new Handle(l => l.LeftWidth, (l, size) => l.LeftWidth = size, 200, double.MaxValue, DefaultLeftWidth, 0),
            ["right"] = new Handle(l => l.RightWidth, (l, size) => l.RightWidth = size, 200, double.MaxValue, DefaultRightWidth, 0),
            ["bottom"] = new Handle(l => l.BottomHeight, (l, size) => l.BottomHeight = size, 80, double.MaxValue, DefaultBottomHeight, 0),
            ["split"] = new Handle(l => l.BottomSplitPercent, (l, size) => l.BottomSplitPercent = size, 15, 85, DefaultBottomSplit, 1),
        };

        // The panels in a dock, by id.
        public List<string> In(Dock dock) => dock switch
        {
            Dock.Left => Left,
            Dock.Right => Right,
            Dock.Bottom2 => BottomRight,
            _ => Bottom,
        };

        // A handle was let go at a size, or double-clicked (a size below 0) to put the default back.
        public void Resize(string kind, double size)
        {
            if (!Handles.TryGetValue(kind, out var handle)) return;
            handle.Set(this, size < 0 ? handle.Default : Math.Round(size, handle.Decimals));
        }

        // What was kept, made whole: lists that are missing are empty, and sizes out of their range are the defaults.
        public SavedLayout Normalize()
        {
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

    // The layout as JSON without reflection, which the trimmed browser build does not keep.
    [JsonSerializable(typeof(SavedLayout))]
    internal partial class RunLayoutJsonContext : JsonSerializerContext
    {
    }
}
