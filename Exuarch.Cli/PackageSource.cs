using System;
using System.IO;
using System.Linq;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public static class PackageSource
    {
        public static MachinePackage Load(string reference)
        {
            if (File.Exists(reference)) return MachinePackage.FromJson(File.ReadAllText(reference));
            var builtIn = BuiltInPackages.All.FirstOrDefault(p => string.Equals(p.Name, reference, StringComparison.OrdinalIgnoreCase));
            if (builtIn != null) return BuiltInPackages.Get(builtIn.Name);
            throw new UsageException($"There is no file '{reference}' and no built in package of that name. Run 'exuarch examples' to list the built in packages.");
        }

        public static (string Name, string Source, ProgramNeeds Needs) Program(MachinePackage package, Arguments arguments)
        {
            var file = arguments.Option("file");
            if (file != null) return (Path.GetFileName(file), File.ReadAllText(file), null);
            if (package.Programs.Count == 0) throw new UsageException($"{package.Name} has no programs. Pass one with --file program.asm.");
            var program = Find(package, arguments.Option("program") ?? "1");
            return (program.Name, program.Source, program.Needs);
        }

        private static PackageProgram Find(MachinePackage package, string wanted)
        {
            if (int.TryParse(wanted, out int number) && number >= 1 && number <= package.Programs.Count) return package.Programs[number - 1];
            var named = package.Programs.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (named != null) return named;
            var choices = string.Join(Environment.NewLine, package.Programs.Select((p, i) => $"  {i + 1}. {p.Name}"));
            throw new UsageException($"{package.Name} has no program '{wanted}'. Its programs are:{Environment.NewLine}{choices}");
        }
    }
}
