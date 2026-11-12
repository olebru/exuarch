using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Exuarch.Core
{
    public enum KeyboardLayoutKind { US, Norwegian }

    public static class KeyboardLayouts
    {
        public const int TableSize = 128;
        public const int ShiftedOffset = TableSize;
        public const int Cells = 2 * TableSize;
        private const int ValuesPerLine = 8;
        private const char Escape = (char)0x1B;
        private const char Backspace = (char)0x08;
        private const char Tab = (char)0x09;
        private const char CarriageReturn = (char)0x0D;

        private sealed record Row(int First, string Plain, string Shifted);

        private static readonly Row[] US =
        {
            new Row(0x02, "1234567890-=", "!@#$%^&*()_+"),
            new Row(0x10, "qwertyuiop[]", "QWERTYUIOP{}"),
            new Row(0x1E, "asdfghjkl;'`", "ASDFGHJKL:\"~"),
            new Row(0x2B, "\\zxcvbnm,./", "|ZXCVBNM<>?"),
            new Row(XtScanCodes.IsoBackslash, "\\", "|"),
        };

        private static readonly Row[] Norwegian =
        {
            new Row(0x02, "1234567890+\\", "!\"#¤%&/()=?`"),
            new Row(0x10, "qwertyuiopå¨", "QWERTYUIOPÅ^"),
            new Row(0x1E, "asdfghjkløæ|", "ASDFGHJKLØÆ§"),
            new Row(0x2B, "'zxcvbnm,.-", "*ZXCVBNM;:_"),
            new Row(XtScanCodes.IsoBackslash, "<", ">"),
        };

        private static readonly Row[] Shared =
        {
            new Row(XtScanCodes.Escape, Escape.ToString(), Escape.ToString()),
            new Row(XtScanCodes.Backspace, Backspace.ToString(), Backspace.ToString()),
            new Row(XtScanCodes.Tab, Tab.ToString(), Tab.ToString()),
            new Row(XtScanCodes.Enter, CarriageReturn.ToString(), CarriageReturn.ToString()),
            new Row(XtScanCodes.Space, " ", " "),
        };

        public static readonly IReadOnlyList<(KeyboardLayoutKind Kind, string Name)> Kinds = new[]
        {
            (KeyboardLayoutKind.US, "US keyboard"),
            (KeyboardLayoutKind.Norwegian, "Norwegian keyboard"),
        };

        public static int[] Values(KeyboardLayoutKind kind)
        {
            var cells = new int[Cells];
            foreach (var row in Shared.Concat(kind == KeyboardLayoutKind.US ? US : Norwegian))
            {
                for (int i = 0; i < row.Plain.Length; i++)
                {
                    cells[row.First + i] = row.Plain[i];
                    cells[ShiftedOffset + row.First + i] = row.Shifted[i];
                }
            }
            return cells;
        }

        public static List<string> Lines(KeyboardLayoutKind kind)
        {
            var values = Values(kind);
            var lines = new List<string>();
            var indent = new string(' ', 8);
            for (int start = 0; start < values.Length; start += ValuesPerLine)
            {
                lines.Add($"{indent}.DATA {string.Join(", ", values.Skip(start).Take(ValuesPerLine).Select(Literal))}");
            }
            return lines;
        }

        private static string Literal(int value)
        {
            if (value == '\'' || value == '\\') return $"'\\{(char)value}'";
            bool printable = (value >= 0x20 && value <= 0x7E) || (value > 0xA0 && value <= 0xFF);
            return printable ? $"'{(char)value}'" : value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
