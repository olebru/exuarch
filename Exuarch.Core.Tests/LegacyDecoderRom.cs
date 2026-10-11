using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Exuarch.Core;
namespace Exuarch.Core.Tests
{
    public class LegacyDecoderRom
    {
        public const int OpCodeAddressSpace = 256;
        private List<MicroInstruction> completeROM;
        private Dictionary<int, List<MicroInstruction>> romByOpCode;
        private Dictionary<string, int> baseAddressByMnemonic;

        public LegacyDecoderRom(string romfilecontent)
        {
            var initialListLine = new List<fileLine>();
            var interMediateListLine = new List<fileLine>();
            var finalListLine = new List<fileLine>();

            int order = -1;
            foreach (var csvLine in romfilecontent.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                order++;
                if (string.IsNullOrWhiteSpace(csvLine)) continue;
                var tokens = csvLine.Split('\t');
                if (tokens.Length < 8)
                {
                    throw new FormatException($"Decoder ROM line {order + 1}: expected 8 tab separated columns, found {tokens.Length}: '{csvLine}'");
                }
                var line = new fileLine();
                line.readOrder = order;
                line.clkFlag = tokens[0];
                line.deviceID = tokens[1];
                line.function = tokens[2];
                line.mnemonic = tokens[3];
                line.negative = tokens[4];
                line.overflow = tokens[5];
                line.carry = tokens[6];
                line.zero = tokens[7];
                if (line.status.Length != 4 || line.status.Any(c => c != '0' && c != '1' && c != 'x'))
                {
                    throw new FormatException($"Decoder ROM line {order + 1}: status columns must each be 0, 1 or x: '{csvLine}'");
                }
                initialListLine.Add(line);
            }

            foreach (var line in initialListLine)
            {
                List<String> validBitCombinations = new List<string>();
                for (int i = 0; i < 16; i++)
                {
                    var possibilty = Convert.ToString(i, 2).PadLeft(4, '0');
                    var cand = new StringBuilder(line.status);
                    for (int n = 0; n < 4; n++)
                    {
                        if (line.status[n] == 'x') cand[n] = possibilty[n];
                    }
                    if (!validBitCombinations.Contains(cand.ToString()))
                        validBitCombinations.Add(cand.ToString());
                }
                foreach (var validCombination in validBitCombinations)
                {
                    var newLine = new fileLine();
                    newLine.readOrder = line.readOrder;
                    newLine.clkFlag = line.clkFlag;
                    newLine.deviceID = line.deviceID;
                    newLine.function = line.function;
                    newLine.mnemonic = line.mnemonic;
                    newLine.negative = validCombination[0].ToString();
                    newLine.overflow = validCombination[1].ToString();
                    newLine.carry = validCombination[2].ToString();
                    newLine.zero = validCombination[3].ToString();
                    interMediateListLine.Add(newLine);
                }
            }
            var listMnemonics = interMediateListLine.Select(l => l.mnemonic).Distinct().ToList();
            baseAddressByMnemonic = new Dictionary<string, int>();
            int addr = 0;
            foreach (var mnemonic in listMnemonics)
            {
                var linesByStatus = interMediateListLine.Where(l => l.mnemonic == mnemonic)
                                                        .OrderBy(l => l.readOrder)
                                                        .GroupBy(l => l.statusAsInt);
                int steps = 0;
                foreach (var statusLines in linesByStatus)
                {
                    int offset = 0;
                    bool firstFound = false;
                    foreach (var line in statusLines)
                    {
                        if (line.clkFlag == "p" && firstFound)
                        {
                            offset++;
                        }
                        if (line.clkFlag == "p")
                        {
                            firstFound = true;
                        }
                        line.instructionBaseAddress = addr;
                        line.mnemonicSeq = offset;
                        finalListLine.Add(line);
                    }
                    steps = Math.Max(steps, offset + 1);
                }
                baseAddressByMnemonic[mnemonic] = addr;
                addr = addr + steps;
            }
            if (addr > OpCodeAddressSpace)
            {
                throw new Exception($"OpCode AddressSpace is exhausted, {addr} opcodes needed but only {OpCodeAddressSpace} available, optimize...");
            }
            completeROM = new List<MicroInstruction>();
            foreach (var line in finalListLine)
            {
                var mc = new MicroInstruction(line.completeOpCode, line.deviceID, line.function, line.mnemonic, false, (line.instructionBaseAddress == line.completeOpCode && line.status == "0000"));
                completeROM.Add(mc);
            }
            romByOpCode = completeROM.GroupBy(m => m.OPCode).ToDictionary(g => g.Key, g => g.ToList());
        }
        public IReadOnlyList<MicroInstruction> MicroInstructions { get { return completeROM; } }
        public byte FetchByteCodeFromMnemonic(string Mnemonic)
        {
            if (!baseAddressByMnemonic.TryGetValue(Mnemonic, out var baseAddress))
            {
                throw new ArgumentException($"Unknown mnemonic '{Mnemonic}', it is not defined in the decoder ROM.");
            }
            return (byte)baseAddress;
        }
        public List<MicroInstruction> FetchInstruction(Byte StatusRegisterValue, Byte InstructionRegisterValue)
        {
            int fullOpCode = ((StatusRegisterValue & 0x0F) << 8) | InstructionRegisterValue;
            return romByOpCode.TryGetValue(fullOpCode, out var microInstructions)
                ? new List<MicroInstruction>(microInstructions)
                : new List<MicroInstruction>();
        }
        public double OpCodeAddressSpaceUsedInPercent()
        {
            var opCodesUsed = baseAddressByMnemonic.Count == 0 ? 0 : completeROM.Max(o => o.OPCode & 0xFF) + 1;
            return Math.Round(((double)opCodesUsed / OpCodeAddressSpace * 100d), 1);
        }
        private class fileLine
        {
            public string carry = string.Empty;
            public string clkFlag = string.Empty;
            public string deviceID = string.Empty;
            public string function = string.Empty;
            public int instructionBaseAddress = 0;
            public string mnemonic = string.Empty;
            public int mnemonicSeq = 0;
            public string negative = string.Empty;
            public string overflow = string.Empty;
            public int readOrder = 0;
            public string zero = string.Empty;
            public int completeOpCode { get { return (statusAsInt << 8) | opCode; } }
            public string status { get { return $"{negative}{overflow}{carry}{zero}"; } }
            public int statusAsInt { get { return Convert.ToInt32(status, 2); } }
            private int opCode { get { return instructionBaseAddress + mnemonicSeq; } }
            public override string ToString()
            {
                return $"{mnemonic},CO{completeOpCode},O{opCode},B{instructionBaseAddress},{clkFlag},{deviceID},{function},{status}";
            }
        }
    }
}
