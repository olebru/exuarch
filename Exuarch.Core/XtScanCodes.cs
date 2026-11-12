using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public static class XtScanCodes
    {
        public const int Escape = 0x01;
        public const int Backspace = 0x0E;
        public const int Tab = 0x0F;
        public const int Enter = 0x1C;
        public const int Control = 0x1D;
        public const int LeftShift = 0x2A;
        public const int RightShift = 0x36;
        public const int Alt = 0x38;
        public const int Space = 0x39;
        public const int Up = 0x48;
        public const int Left = 0x4B;
        public const int Right = 0x4D;
        public const int Down = 0x50;
        public const int IsoBackslash = 0x56;

        private static readonly string[] DigitRow = { "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "Minus", "Equal" };
        private static readonly string[] TopRow = { "KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI", "KeyO", "KeyP", "BracketLeft", "BracketRight" };
        private static readonly string[] HomeRow = { "KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK", "KeyL", "Semicolon", "Quote", "Backquote" };
        private static readonly string[] BottomRow = { "Backslash", "KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM", "Comma", "Period", "Slash" };
        private static readonly string[] FunctionRow = { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10" };

        private static readonly Dictionary<string, int> ByKeyCode = Build();

        public static int? For(string keyCode)
        {
            return keyCode != null && ByKeyCode.TryGetValue(keyCode, out var scanCode) ? scanCode : null;
        }

        public static IEnumerable<KeyValuePair<string, int>> All { get { return ByKeyCode; } }

        private static Dictionary<string, int> Build()
        {
            var codes = new Dictionary<string, int>
            {
                ["Escape"] = Escape,
                ["Backspace"] = Backspace,
                ["Tab"] = Tab,
                ["Enter"] = Enter,
                ["NumpadEnter"] = Enter,
                ["ControlLeft"] = Control,
                ["ControlRight"] = Control,
                ["ShiftLeft"] = LeftShift,
                ["ShiftRight"] = RightShift,
                ["NumpadMultiply"] = 0x37,
                ["AltLeft"] = Alt,
                ["AltRight"] = Alt,
                ["Space"] = Space,
                ["CapsLock"] = 0x3A,
                ["NumLock"] = 0x45,
                ["ScrollLock"] = 0x46,
                ["Numpad7"] = 0x47,
                ["Home"] = 0x47,
                ["Numpad8"] = Up,
                ["ArrowUp"] = Up,
                ["Numpad9"] = 0x49,
                ["PageUp"] = 0x49,
                ["NumpadSubtract"] = 0x4A,
                ["Numpad4"] = Left,
                ["ArrowLeft"] = Left,
                ["Numpad5"] = 0x4C,
                ["Numpad6"] = Right,
                ["ArrowRight"] = Right,
                ["NumpadAdd"] = 0x4E,
                ["Numpad1"] = 0x4F,
                ["End"] = 0x4F,
                ["Numpad2"] = Down,
                ["ArrowDown"] = Down,
                ["Numpad3"] = 0x51,
                ["PageDown"] = 0x51,
                ["Numpad0"] = 0x52,
                ["Insert"] = 0x52,
                ["NumpadDecimal"] = 0x53,
                ["Delete"] = 0x53,
                ["IntlBackslash"] = IsoBackslash,
            };
            AddRow(codes, DigitRow, 0x02);
            AddRow(codes, TopRow, 0x10);
            AddRow(codes, HomeRow, 0x1E);
            AddRow(codes, BottomRow, 0x2B);
            AddRow(codes, FunctionRow, 0x3B);
            return codes;
        }

        private static void AddRow(Dictionary<string, int> codes, string[] keyCodes, int first)
        {
            foreach (var (keyCode, offset) in keyCodes.Select((k, i) => (k, i))) codes[keyCode] = first + offset;
        }
    }
}
