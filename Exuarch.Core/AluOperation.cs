using System;
namespace Exuarch.Core
{
    internal abstract class AluOperation
    {
        private const int Mask = Bus.Mask;
        private const int SignBit = Bus.SignBit;

        public static readonly AluOperation[] All =
        {
            new Add("add", addsCarry: false), new Subtract("sub", drivesBus: true, subtractsCarry: false), new Subtract("cmp", drivesBus: false, subtractsCarry: false),
            new Bitwise("and", (x, y) => x & y), new Bitwise("orr", (x, y) => x | y), new Bitwise("eor", (x, y) => x ^ y),
            new ShiftLeft(), new ShiftRight(),
            new Add("adc", addsCarry: true), new Subtract("sbc", drivesBus: true, subtractsCarry: true),
        };

        protected AluOperation(string name, bool drivesBus = true)
        {
            Name = name;
            DrivesBus = drivesBus;
        }
        public string Name { get; }
        public bool DrivesBus { get; }

        protected abstract int Compute(int x, int y, int carryIn, out int carryAndOverflow);

        public int Run(int x, int y, bool carryIn, out int status)
        {
            int result = Compute(x & Mask, y & Mask, carryIn ? 1 : 0, out status);
            status |= (result == 0 ? StatusRegister.ZeroFlag : 0) | ((result & SignBit) != 0 ? StatusRegister.NegativeFlag : 0);
            return result;
        }

        private sealed class Add : AluOperation
        {
            private readonly bool addsCarry;
            public Add(string name, bool addsCarry) : base(name) { this.addsCarry = addsCarry; }
            protected override int Compute(int x, int y, int carryIn, out int flags)
            {
                int sum = x + y + (addsCarry ? carryIn : 0), result = sum & Mask;
                flags = (sum > Mask ? StatusRegister.CarryFlag : 0) | (((x ^ result) & (y ^ result) & SignBit) != 0 ? StatusRegister.OverflowFlag : 0);
                return result;
            }
        }

        private sealed class Subtract : AluOperation
        {
            private readonly bool subtractsCarry;
            public Subtract(string name, bool drivesBus, bool subtractsCarry) : base(name, drivesBus) { this.subtractsCarry = subtractsCarry; }
            protected override int Compute(int x, int y, int carryIn, out int flags)
            {
                int subtrahend = y + (subtractsCarry ? carryIn : 0), result = (x - subtrahend) & Mask;
                flags = (x < subtrahend ? StatusRegister.CarryFlag : 0) | (((x ^ y) & (x ^ result) & SignBit) != 0 ? StatusRegister.OverflowFlag : 0);
                return result;
            }
        }

        private sealed class Bitwise : AluOperation
        {
            private readonly Func<int, int, int> combine;
            public Bitwise(string name, Func<int, int, int> combine) : base(name) { this.combine = combine; }
            protected override int Compute(int x, int y, int carryIn, out int flags)
            {
                flags = 0;
                return combine(x, y);
            }
        }

        private sealed class ShiftLeft : AluOperation
        {
            public ShiftLeft() : base("lsl") { }
            protected override int Compute(int x, int y, int carryIn, out int flags)
            {
                int shift = y & 15;
                flags = shift > 0 && ((x >> (16 - shift)) & 1) != 0 ? StatusRegister.CarryFlag : 0;
                return (x << shift) & Mask;
            }
        }
        private sealed class ShiftRight : AluOperation
        {
            public ShiftRight() : base("lsr") { }
            protected override int Compute(int x, int y, int carryIn, out int flags)
            {
                int shift = y & 15;
                flags = shift > 0 && ((x >> (shift - 1)) & 1) != 0 ? StatusRegister.CarryFlag : 0;
                return x >> shift;
            }
        }
    }
}
