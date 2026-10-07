namespace Exuarch.Web.Workbench
{
    // What the new machine dialog asks; it keeps what was typed and picked between openings.
    public sealed class NewMachineForm
    {
        public static readonly (string Value, string Title, string Description)[] Starts =
        {
            ("minimal", "Minimal CPU", "A bus, program counter, memory, instruction register, status register and clock, with fetch, NOP, JMP and HLT. It runs straight away."),
            ("empty", "Empty", "One bus and nothing else. Add the devices, decoder and microcode yourself."),
            ("copy", "Copy of the current machine", "Everything in the machine you have open now, with its programs, under the new name."),
        };

        public string Name { get; set; } = "My machine";
        public string Start { get; set; } = "minimal";
    }
}
