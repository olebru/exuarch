namespace Exuarch.Core
{
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
