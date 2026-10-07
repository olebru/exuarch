using System;

namespace Exuarch.Web.Components
{
    // How the views write numbers: a 16 bit word in hex, a value in bits, and coordinates and lengths for inline styles
    // and SVG, to a tenth of a pixel. Every .razor file has these (see _Imports.razor).
    public static class Formats
    {
        // Every value is a 16 bit word: four hex digits.
        public const int Digits = 4;

        public static string Hex(int value) => value.ToString("X4");
        // A value that fits in a byte, as a keypad's keys or an LCD cell: 0x and two hex digits.
        public static string HexByte(int value) => $"0x{value:X2}";
        public static string Bits(int value, int count) => Convert.ToString(value, 2).PadLeft(count, '0');
        public static string N(double value) => FormattableString.Invariant($"{value:0.#}");
        public static string Px(double value) => FormattableString.Invariant($"{value:0.#}px");
    }
}
