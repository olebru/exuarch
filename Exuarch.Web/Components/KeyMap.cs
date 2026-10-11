using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;

namespace Exuarch.Web.Components
{
    public readonly record struct KeyChord(string Key, bool Mod, bool Shift)
    {
        public static KeyChord Parse(string text)
        {
            var parts = text.Split('+');
            var modifiers = parts.Take(parts.Length - 1).ToList();
            return new KeyChord(parts[^1], modifiers.Contains("Mod"), modifiers.Contains("Shift"));
        }

        public bool Matches(KeyboardEventArgs e)
        {
            return (!Mod || e.CtrlKey || e.MetaKey) && (!Shift || e.ShiftKey) && string.Equals(e.Key, Key, KeyComparison);
        }

        private StringComparison KeyComparison => Key.Length == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static bool OnMac { get; set; }

        public IEnumerable<string> Caps(bool mac)
        {
            if (Mod) yield return mac ? "⌘" : "Ctrl";
            if (Shift) yield return mac ? "⇧" : "Shift";
            yield return Key switch
            {
                "Delete" => "Del",
                "Backspace" => "⌫",
                "Escape" => "Esc",
                _ => Key.Length == 1 ? Key.ToUpperInvariant() : Key,
            };
        }

        public string Label(bool mac) => string.Join(mac ? "" : "+", Caps(mac));
        public static string Label(string chord) => Parse(chord).Label(OnMac);
    }

    public sealed class KeyMap
    {
        private readonly List<(KeyChord Chord, Func<Task> Action)> bindings = new List<(KeyChord, Func<Task>)>();

        public KeyMap On(string chord, Func<Task> action)
        {
            bindings.Add((KeyChord.Parse(chord), action));
            return this;
        }

        public KeyMap On(string chord, Action action)
        {
            return On(chord, () =>
            {
                action();
                return Task.CompletedTask;
            });
        }

        public Task Dispatch(KeyboardEventArgs e)
        {
            var binding = bindings.FirstOrDefault(b => b.Chord.Matches(e));
            return binding.Action?.Invoke() ?? Task.CompletedTask;
        }
    }
}
