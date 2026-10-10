using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class AssembleCommand : ICommand
    {
        private const int WordsColumnWidth = 15;

        public string Name { get { return "assemble"; } }
        public string Usage { get { return "assemble <package> [--program <name or number> | --file <program.asm>]"; } }
        public string Summary { get { return "Assemble a program and print the listing: address, words, source"; } }
        public IReadOnlyCollection<string> Options { get { return new[] { "program", "file" }; } }

        public int Execute(Arguments arguments, TextWriter output)
        {
            arguments.NoMorePositionalThan(1);
            var package = PackageSource.Load(arguments.Positional(0, "package"));
            var program = PackageSource.Program(package, arguments);
            var report = new Report(output);
            var result = Assemble(package, program.Source, report);
            if (!result.Success) return ExitCode.Problems;
            foreach (var line in result.Listing) output.WriteLine(Listing(line));
            output.WriteLine($"{program.Name}: {Report.Plural(result.Cells.Length, "word")}.");
            return ExitCode.Success;
        }

        public static AssemblyResult Assemble(MachinePackage package, string source, Report report)
        {
            var result = Build.Empty(package).Assembler.Analyze(source ?? "");
            report.Diagnostics(result.Diagnostics);
            return result;
        }

        private static string Listing(ListingLine line)
        {
            var words = string.Join(" ", (line.Cells ?? new int[0]).Select(c => c.ToString("X4")));
            var address = line.Cells?.Length > 0 || line.Label != null ? line.Address.ToString("X4") : "    ";
            return $"{address}  {words.PadRight(WordsColumnWidth)}  {line.Text?.Trim()}";
        }
    }
}
