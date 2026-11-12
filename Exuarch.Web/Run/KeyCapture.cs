using System;
using System.Collections.Generic;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    public sealed class KeyCapture
    {
        public const int EscapeTapsToRelease = 3;
        public static readonly TimeSpan EscapeTapWindow = TimeSpan.FromMilliseconds(600);

        private static readonly Dictionary<string, Keypad.Keys> KeypadKeys = new Dictionary<string, Keypad.Keys>
        {
            ["ArrowUp"] = Keypad.Keys.Up,
            ["ArrowDown"] = Keypad.Keys.Down,
            ["ArrowLeft"] = Keypad.Keys.Left,
            ["ArrowRight"] = Keypad.Keys.Right,
            [" "] = Keypad.Keys.Space,
        };

        private readonly TimeProvider time;
        private int escapeTaps;
        private long lastEscapeTap;

        public KeyCapture(TimeProvider time = null)
        {
            this.time = time ?? TimeProvider.System;
        }

        public Keypad Keypad { get; private set; }
        public Keyboard Keyboard { get; private set; }
        public bool Active => Keypad != null || Keyboard != null;

        public void Capture(Keypad keypad)
        {
            Release();
            Keypad = keypad;
        }

        public void Capture(Keyboard keyboard)
        {
            Release();
            Keyboard = keyboard;
        }

        public void Release()
        {
            Keypad?.ReleaseAll();
            Keyboard?.ReleaseAll();
            Keypad = null;
            Keyboard = null;
            escapeTaps = 0;
        }

        public bool KeyDown(string key, string code = null, bool repeat = false)
        {
            if (Keyboard != null) return KeyboardKeyDown(code, repeat);
            if (Keypad == null) return false;
            if (key == "Escape")
            {
                Release();
                return true;
            }
            if (!KeypadKeys.TryGetValue(key, out var pressed)) return false;
            Keypad.Press(pressed);
            return true;
        }

        public void KeyUp(string key, string code = null)
        {
            if (Keyboard != null && XtScanCodes.For(code) is int scanCode) Keyboard.Release(scanCode);
            else if (Keypad != null && KeypadKeys.TryGetValue(key, out var released)) Keypad.Release(released);
        }

        private bool KeyboardKeyDown(string code, bool repeat)
        {
            if (XtScanCodes.For(code) is int scanCode) Keyboard.Press(scanCode);
            if (!repeat) CountEscapeTap(code == "Escape");
            if (escapeTaps == EscapeTapsToRelease) Release();
            return true;
        }

        private void CountEscapeTap(bool escape)
        {
            bool soonAfterTheLast = escapeTaps > 0 && time.GetElapsedTime(lastEscapeTap) <= EscapeTapWindow;
            escapeTaps = !escape ? 0 : soonAfterTheLast ? escapeTaps + 1 : 1;
            lastEscapeTap = time.GetTimestamp();
        }
    }
}
