using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Exuarch.Cli
{
    public class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    public class Arguments
    {
        private readonly List<string> positional = new List<string>();
        private readonly Dictionary<string, List<string>> options = new Dictionary<string, List<string>>();

        public static Arguments Parse(IEnumerable<string> args, IReadOnlyCollection<string> knownOptions)
        {
            var parsed = new Arguments();
            string pending = null;
            foreach (var arg in args)
            {
                if (pending != null) { parsed.Add(pending, arg); pending = null; }
                else if (arg.StartsWith("--")) pending = Known(arg.Substring(2), knownOptions);
                else parsed.positional.Add(arg);
            }
            if (pending != null) throw new UsageException($"--{pending} needs a value.");
            return parsed;
        }

        private static string Known(string option, IReadOnlyCollection<string> knownOptions)
        {
            if (knownOptions.Contains(option)) return option;
            var allowed = knownOptions.Count == 0 ? "This command takes no options." : "Its options are " + string.Join(", ", knownOptions.Select(o => "--" + o)) + ".";
            throw new UsageException($"Unknown option --{option}. {allowed}");
        }

        private void Add(string option, string value)
        {
            if (!options.TryGetValue(option, out var values)) options[option] = values = new List<string>();
            values.Add(value);
        }

        public string Positional(int index, string name)
        {
            return index < positional.Count ? positional[index] : throw new UsageException($"Missing <{name}>.");
        }

        public string OptionalPositional(int index)
        {
            return index < positional.Count ? positional[index] : null;
        }

        public void NoMorePositionalThan(int count)
        {
            if (positional.Count > count) throw new UsageException($"Unexpected argument '{positional[count]}'.");
        }

        public string Option(string name)
        {
            return options.TryGetValue(name, out var values) ? values[^1] : null;
        }

        public IReadOnlyList<string> Options(string name)
        {
            return options.TryGetValue(name, out var values) ? values : new List<string>();
        }

        public int Number(string name, int fallback)
        {
            var text = Option(name);
            if (text == null) return fallback;
            return ParseNumber(text, $"--{name}");
        }

        public static int ParseNumber(string text, string what)
        {
            var digits = text.Replace("_", "").Replace(",", "");
            bool hex = digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            var style = hex ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (int.TryParse(hex ? digits.Substring(2) : digits, style, CultureInfo.InvariantCulture, out int value) && value >= 0) return value;
            throw new UsageException($"{what} has to be a whole number such as 1000 or 0x3E8, not '{text}'.");
        }
    }
}
