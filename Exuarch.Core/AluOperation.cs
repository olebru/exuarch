using System;
namespace Exuarch.Core
{
    // One operation of the ALU, a control line of its own. Compute gives the 16 bit result of x and y and the C and V
    // flags it sets; Z and N follow from the result the same way for every operation.
    internal abstract class AluOperation
    {
        private const int Mask = Bus.Mask;
        private const int SignBit = Bus.SignBit;

        // Every operation, in the order the ALU lists its control lines.
        public static readonly AluOperation[] All =
        {
            new Add(), new Subtract("sub", drivesBus: true), new Subtract("cmp", drivesBus: false),
            new Bitwise("and", (x, y) => x & y), new Bitwise("orr", (x, y) => x | y), new Bitwise("eor", (x, y) => x ^ y),
            new ShiftLeft(), new ShiftRight(),
        };

        protected AluOperation(string name, bool drivesBus = true)
        {
            Name = name;
            DrivesBus = drivesBus;
        }
        public string Name { get; }
        // False for an operation that only sets the flags (cmp).
        public bool DrivesBus { get; }

        protected abstract int Compute(int x, int y, out int carryAndOverflow);

        // The result, and every flag: Z when the result is 0 and N from its top bit, the sign in two's complement.
        public int Run(int x, int y, out int status)
        {
            int result = Compute(x & Mask, y & Mask, out status);
            status |= (result == 0 ? StatusRegister.ZeroFlag : 0) | ((result & SignBit) != 0 ? StatusRegister.NegativeFlag : 0);
            return result;
        }

        // a + b. C when the sum carries out, V on signed overflow.
        private sealed class Add : AluOperation
        {
            public Add() : base("add") { }
            protected override int Compute(int x, int y, out int flags)
            {
                int sum = x + y, result = sum & Mask;
                flags = (sum > Mask ? StatusRegister.CarryFlag : 0) | (((x ^ result) & (y ^ result) & SignBit) != 0 ? StatusRegister.OverflowFlag : 0);
                return result;
            }
        }

        // a - b. C when a < b unsigned (a borrow), V on signed overflow.
        private sealed class Subtract : AluOperation
        {
            public Subtract(string name, bool drivesBus) : base(name, drivesBus) { }
            protected override int Compute(int x, int y, out int flags)
            {
                int result = (x - y) & Mask;
                flags = (x < y ? StatusRegister.CarryFlag : 0) | (((x ^ y) & (x ^ result) & SignBit) != 0 ? StatusRegister.OverflowFlag : 0);
                return result;
            }
        }

        // and, orr and eor clear C and V.
        private sealed class Bitwise : AluOperation
        {
            private readonly Func<int, int, int> combine;
            public Bitwise(string name, Func<int, int, int> combine) : base(name) { this.combine = combine; }
            protected override int Compute(int x, int y, out int flags)
            {
                flags = 0;
                return combine(x, y);
            }
        }

        // a shifted by b (0-15). C is the last bit shifted out.
        private sealed class ShiftLeft : AluOperation
        {
            public ShiftLeft() : base("lsl") { }
            protected override int Compute(int x, int y, out int flags)
            {
                int shift = y & 15;
                flags = shift > 0 && ((x >> (16 - shift)) & 1) != 0 ? StatusRegister.CarryFlag : 0;
                return (x << shift) & Mask;
            }
        }
        private sealed class ShiftRight : AluOperation
        {
            public ShiftRight() : base("lsr") { }
            protected override int Compute(int x, int y, out int flags)
            {
                int shift = y & 15;
                flags = shift > 0 && ((x >> (shift - 1)) & 1) != 0 ? StatusRegister.CarryFlag : 0;
                return x >> shift;
            }
        }
    }
}
