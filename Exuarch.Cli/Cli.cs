using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public static class ExitCode
    {
        public const int Success = 0;
        public const int Problems = 1;
        public const int Usage = 2;
    }

    public interface ICommand
    {
        string Name { get; }
        string Usage { get; }
        string Summary { get; }
        IReadOnlyCollection<string> Options { get; }
        int Execute(Arguments arguments, TextWriter output);
    }

    public static class Cli
    {
        public static readonly IReadOnlyList<ICommand> Commands = new ICommand[]
        {
            new ExamplesCommand(), new ExportCommand(), new ValidateCommand(), new AssembleCommand(), new RunCommand(), new DocsCommand(),
        };

        private static readonly string[] HelpWords = { "help", "--help", "-h" };

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0 || HelpWords.Contains(args[0])) { output.Write(Usage()); return ExitCode.Success; }
            if (args[0] == "--version") { output.WriteLine(Version); return ExitCode.Success; }
            var command = Commands.FirstOrDefault(c => c.Name == args[0]);
            if (command == null)
            {
                error.WriteLine($"Unknown command '{args[0]}'.");
                error.Write(Usage());
                return ExitCode.Usage;
            }
            return Execute(command, args.Skip(1), output, error);
        }

        private static int Execute(ICommand command, IEnumerable<string> args, TextWriter output, TextWriter error)
        {
            try
            {
                return command.Execute(Arguments.Parse(args, command.Options), output);
            }
            catch (UsageException e)
            {
                error.WriteLine(e.Message);
                error.WriteLine($"Usage: exuarch {command.Usage}");
                return ExitCode.Usage;
            }
            catch (MachineDefinitionException e)
            {
                foreach (var problem in e.Errors) output.WriteLine($"error: {problem}");
                return ExitCode.Problems;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error.WriteLine(e.Message);
                return ExitCode.Problems;
            }
        }

        public static string Version
        {
            get
            {
                var version = typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                int build = version.IndexOf('+');
                return build < 0 ? version : version.Substring(0, build);
            }
        }

        public static string Usage()
        {
            var lines = Commands.Select(c => $"  exuarch {c.Usage}{Environment.NewLine}      {c.Summary}");
            return $"exuarch {Version}: check, assemble and run ExuArch machine packages.{Environment.NewLine}{Environment.NewLine}"
                   + string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine
                   + "<package> is a package .json file exported from exuarch.com, or the name of a built in package such as BYOC-16." + Environment.NewLine
                   + "Exit codes: 0 when all is well, 1 when the package or program has errors, 2 when the command line is wrong." + Environment.NewLine;
        }
    }
}
