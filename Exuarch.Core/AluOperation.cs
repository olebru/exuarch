using System;
namespace Exuarch.Core
{
    internal abstract class AluOperation
    {
        private const int Mask = Bus.Mask;
        private const int SignBit = Bus.SignBit;

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
        public bool DrivesBus { get; }

        protected abstract int Compute(int x, int y, out int carryAndOverflow);

        public int Run(int x, int y, out int status)
        {
            int result = Compute(x & Mask, y & Mask, out status);
            status |= (result == 0 ? StatusRegister.ZeroFlag : 0) | ((result & SignBit) != 0 ? StatusRegister.NegativeFlag : 0);
            return result;
        }

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
