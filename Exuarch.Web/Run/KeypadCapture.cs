using System.Collections.Generic;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // While a keypad has the keyboard, the arrows and space go to it and Esc gives the keyboard back.
    public sealed class KeypadCapture
    {
        private static readonly Dictionary<string, Keypad.Keys> KeypadKeys = new Dictionary<string, Keypad.Keys>
        {
            ["ArrowUp"] = Keypad.Keys.Up,
            ["ArrowDown"] = Keypad.Keys.Down,
            ["ArrowLeft"] = Keypad.Keys.Left,
            ["ArrowRight"] = Keypad.Keys.Right,
            [" "] = Keypad.Keys.Space,
        };

        // The keypad that has the keyboard, if one has.
        public Keypad Keypad { get; private set; }

        // Hands the keyboard to a keypad, or with null takes it back; the keypad that had it lets its keys go.
        public void Capture(Keypad keypad)
        {
            Keypad?.ReleaseAll();
            Keypad = keypad;
        }

        // A key went down: true when the keypad took it, so it is not a shortcut too.
        public bool KeyDown(string key)
        {
            if (Keypad == null) return false;
            if (key == "Escape")
            {
                Capture(null);
                return true;
            }
            if (!KeypadKeys.TryGetValue(key, out var pressed)) return false;
            Keypad.Press(pressed);
            return true;
        }

        public void KeyUp(string key)
        {
            if (Keypad != null && KeypadKeys.TryGetValue(key, out var released)) Keypad.Release(released);
        }
    }
}
