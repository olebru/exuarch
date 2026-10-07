namespace Exuarch.Core
{
    // What an operand in a register slot names: R0 up to R(Count - 1) of the machine's register file, or nothing
    // when Number is null. The assembler and the editor's hover both read register operands through this.
    internal readonly record struct RegisterOperand(int? Number, int Count)
    {
        public static RegisterOperand Of(SourceToken operand, int count)
        {
            return new RegisterOperand(Assembler.TryRegister(operand, count, out var register) ? register : null, count);
        }

        public bool MachineHasNone { get { return Count == 0; } }
        public string Names { get { return $"R0 to R{Count - 1}"; } }
    }
}
