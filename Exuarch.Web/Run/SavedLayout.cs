using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Exuarch.Web.Run
{
    public enum Dock { Left, Right, Bottom, Bottom2 }

    public static class DockNames
    {
        public static string Key(this Dock dock) => dock.ToString().ToLowerInvariant();

        public static string Fold(this Dock dock) => dock == Dock.Bottom2 ? Dock.Bottom.Key() : dock.Key();
    }

    public sealed class SavedLayout
    {
        public const double DefaultLeftWidth = 340, DefaultRightWidth = 360, DefaultBottomHeight = 260, DefaultBottomSplit = 50;

        public List<string> Left { get; set; } = new List<string>();
        public List<string> Right { get; set; } = new List<string>();
        public List<string> Bottom { get; set; } = new List<string>();
        public List<string> BottomRight { get; set; } = new List<string>();
        public bool BottomSplit { get; set; }
        public double BottomSplitPercent { get; set; } = DefaultBottomSplit;
        public double LeftWidth { get; set; } = DefaultLeftWidth;
        public double RightWidth { get; set; } = DefaultRightWidth;
        public double BottomHeight { get; set; } = DefaultBottomHeight;

        private sealed record Handle(Func<SavedLayout, double> Get, Action<SavedLayout, double> Set, double Min, double Max, double Default, int Decimals);

        private static readonly Dictionary<string, Handle> Handles = new Dictionary<string, Handle>
        {
            ["left"] = new Handle(l => l.LeftWidth, (l, size) => l.LeftWidth = size, 200, double.MaxValue, DefaultLeftWidth, 0),
            ["right"] = new Handle(l => l.RightWidth, (l, size) => l.RightWidth = size, 200, double.MaxValue, DefaultRightWidth, 0),
            ["bottom"] = new Handle(l => l.BottomHeight, (l, size) => l.BottomHeight = size, 80, double.MaxValue, DefaultBottomHeight, 0),
            ["split"] = new Handle(l => l.BottomSplitPercent, (l, size) => l.BottomSplitPercent = size, 15, 85, DefaultBottomSplit, 1),
        };

        public List<string> In(Dock dock) => dock switch
        {
            Dock.Left => Left,
            Dock.Right => Right,
            Dock.Bottom2 => BottomRight,
            _ => Bottom,
        };

        public void Resize(string kind, double size)
        {
            if (!Handles.TryGetValue(kind, out var handle)) return;
            bool restoresDefault = size < 0;
            handle.Set(this, restoresDefault ? handle.Default : Math.Round(size, handle.Decimals));
        }

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

    [JsonSerializable(typeof(SavedLayout))]
    internal partial class RunLayoutJsonContext : JsonSerializerContext
    {
    }
}
