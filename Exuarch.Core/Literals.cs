using System.Collections.Generic;
namespace Exuarch.Core
{
    internal readonly record struct QuotedText(int[] Values, string Error)
    {
        public QuotedText AsCharacter()
        {
            return Error != null || Values.Length == 1 ? this : new QuotedText(null, "a character literal holds exactly one character");
        }
    }

    internal static class QuotedReader
    {
        private static readonly Dictionary<char, int> Escapes = new Dictionary<char, int>
        {
            ['n'] = 10, ['t'] = 9, ['0'] = 0, ['\\'] = '\\', ['"'] = '"', ['\''] = '\'',
        };

        public static QuotedText Read(SourceReader reader, char quote)
        {
            var values = new List<int>();
            while (!reader.AtEnd)
            {
                if (reader.Take(quote)) return new QuotedText(values.ToArray(), null);
                var error = ReadCharacter(reader, values);
                if (error != null) return new QuotedText(null, error);
            }
            return new QuotedText(null, $"missing closing {quote}");
        }

        private static string ReadCharacter(SourceReader reader, List<int> values)
        {
            char c = reader.Advance();
            if (c == '\\' && !reader.AtEnd) return ReadEscape(reader, values);
            if (c > 0xFF) return $"'{c}' is not a Latin-1 character";
            values.Add(c);
            return null;
        }

        private static string ReadEscape(SourceReader reader, List<int> values)
        {
            char next = reader.Advance();
            if (!Escapes.TryGetValue(next, out var value)) return $"unknown escape '\\{next}'";
            values.Add(value);
            return null;
        }
    }

    internal readonly record struct NumberLiteral(int Value, string Error)
    {
        public static NumberLiteral Parse(string written, string digits, bool negative)
        {
            if (!AssemblyParser.TryParseNumber(digits, out var number)) return new NumberLiteral(0, $"'{written}' is not a number, write 123, 0x7B, -5 or 'A'");
            if (negative && number > 32768) return new NumberLiteral(0, $"'{written}' does not fit in 16 bits, the lowest is -32768");
            return new NumberLiteral(negative ? -number & Bus.Mask : number, null);
        }
    }
}
