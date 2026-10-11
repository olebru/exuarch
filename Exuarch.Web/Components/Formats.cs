using System;

namespace Exuarch.Web.Components
{
    public static class Formats
    {
        public const int Digits = 4;

        public static string Hex(int value) => value.ToString("X4");
        public static string HexByte(int value) => $"0x{value:X2}";
        public static string Bits(int value, int count) => Convert.ToString(value, 2).PadLeft(count, '0');
        public static string N(double value) => FormattableString.Invariant($"{value:0.#}");
        public static string Px(double value) => FormattableString.Invariant($"{value:0.#}px");
    }
}
