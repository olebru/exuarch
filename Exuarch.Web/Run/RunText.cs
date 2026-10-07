using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // How the Run view writes what is particular to it: clock speeds, decoder ROM addresses and instructions.
    public static class RunText
    {
        // A speed in a few characters for the slider: 16 Hz, 1.2 kHz, 500 kHz, 1 MHz.
        public static string ShortHz(int hz)
        {
            if (hz >= 1_000_000) return $"{hz / 1_000_000.0:0.#} MHz";
            return hz < 1000 ? $"{hz} Hz" : $"{hz / 1000.0:0.#} kHz";
        }

        // A measured speed, with two decimals: 16.0 Hz, 1.20 kHz.
        public static string FormatHz(double hz)
        {
            if (hz >= 1_000_000) return $"{hz / 1_000_000:0.00} MHz";
            if (hz >= 1_000) return $"{hz / 1_000:0.00} kHz";
            return $"{hz:0.0} Hz";
        }

        public static string RomHex(int address) => address.ToString("X5");

        // An instruction as it reads in the listing: the mnemonic and its operands.
        public static string SourceText(ListingLine line)
        {
            if (line == null) return "";
            var operands = line.Operands.Length == 0 ? "" : " " + string.Join(", ", line.Operands);
            return $"{line.Mnemonic}{operands}";
        }
    }
}
