using System.Collections.Generic;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
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

        public Keypad Keypad { get; private set; }

        public void Capture(Keypad keypad)
        {
            Keypad?.ReleaseAll();
            Keypad = keypad;
        }

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
