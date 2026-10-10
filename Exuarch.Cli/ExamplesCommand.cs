using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class ExamplesCommand : ICommand
    {
        public string Name { get { return "examples"; } }
        public string Usage { get { return "examples"; } }
        public string Summary { get { return "List the built in packages and their programs"; } }
        public IReadOnlyCollection<string> Options { get { return Array.Empty<string>(); } }

        public int Execute(Arguments arguments, TextWriter output)
        {
            arguments.NoMorePositionalThan(0);
            foreach (var package in BuiltInPackages.All)
            {
                output.WriteLine($"{package.Name} ({BuiltInPackages.Level(package.Name)}): {package.Summary}");
                foreach (var (program, index) in package.Programs.Select((p, i) => (p, i))) output.WriteLine($"  {index + 1}. {program.Name}");
            }
            return ExitCode.Success;
        }
    }
}
