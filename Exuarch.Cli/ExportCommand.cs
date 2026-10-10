using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class ExportCommand : ICommand
    {
        public string Name { get { return "export"; } }
        public string Usage { get { return "export <built in package> [file.json]"; } }
        public string Summary { get { return "Write a built in package as JSON, to start a machine of your own from"; } }
        public IReadOnlyCollection<string> Options { get { return Array.Empty<string>(); } }

        public int Execute(Arguments arguments, TextWriter output)
        {
            arguments.NoMorePositionalThan(2);
            var name = arguments.Positional(0, "built in package");
            var package = BuiltInPackages.All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                          ?? throw new UsageException($"There is no built in package '{name}'. Run 'exuarch examples' to list them.");
            var json = BuiltInPackages.Get(package.Name).ToJson();
            var file = arguments.OptionalPositional(1);
            if (file == null) { output.WriteLine(json); return ExitCode.Success; }
            if (File.Exists(file)) throw new UsageException($"{file} already exists. Pick another name, or remove it first.");
            File.WriteAllText(file, json + Environment.NewLine);
            output.WriteLine($"Wrote {package.Name} to {file}.");
            return ExitCode.Success;
        }
    }
}
