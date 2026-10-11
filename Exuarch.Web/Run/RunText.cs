using Exuarch.Core;

namespace Exuarch.Web.Run
{
    public static class RunText
    {
        public static string ShortHz(int hz)
        {
            if (hz >= 1_000_000) return $"{hz / 1_000_000.0:0.#} MHz";
            return hz < 1000 ? $"{hz} Hz" : $"{hz / 1000.0:0.#} kHz";
        }

        public static string FormatHz(double hz)
        {
            if (hz >= 1_000_000) return $"{hz / 1_000_000:0.00} MHz";
            if (hz >= 1_000) return $"{hz / 1_000:0.00} kHz";
            return $"{hz:0.0} Hz";
        }

        public static string RomHex(int address) => address.ToString("X5");

        public static string SourceText(ListingLine line)
        {
            if (line == null) return "";
            var operands = line.Operands.Length == 0 ? "" : " " + string.Join(", ", line.Operands);
            return $"{line.Mnemonic}{operands}";
        }
    }
}
