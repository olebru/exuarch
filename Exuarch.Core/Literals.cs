using System.Collections.Generic;
namespace Exuarch.Core
{
    // The characters of a quoted string or character literal, or why it can not be read.
    internal readonly record struct QuotedText(int[] Values, string Error)
    {
        // A character literal is quoted text of exactly one character.
        public QuotedText AsCharacter()
        {
            return Error != null || Values.Length == 1 ? this : new QuotedText(null, "a character literal holds exactly one character");
        }
    }

    // Reads "strings" and 'c'haracters, which share their escapes: \n \t \0 \\ \" \'.
    internal static class QuotedReader
    {
        private static readonly Dictionary<char, int> Escapes = new Dictionary<char, int>
        {
            ['n'] = 10, ['t'] = 9, ['0'] = 0, ['\\'] = '\\', ['"'] = '"', ['\''] = '\'',
        };

        // Reads from after the opening quote up to and including the closing one.
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

        // Adds one character or escape to values, or says why it can not.
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

    // A number literal: its 16 bit value, or why it is not one.
    internal readonly record struct NumberLiteral(int Value, string Error)
    {
        // written is the literal as it appears, with any # or -; digits is the part after them.
        public static NumberLiteral Parse(string written, string digits, bool negative)
        {
            if (!AssemblyParser.TryParseNumber(digits, out var number)) return new NumberLiteral(0, $"'{written}' is not a number, write 123, 0x7B, -5 or 'A'");
            if (negative && number > 32768) return new NumberLiteral(0, $"'{written}' does not fit in 16 bits, the lowest is -32768");
            // Negative numbers are stored as the 16 bit two's complement: -1 is 0xFFFF.
            return new NumberLiteral(negative ? -number & Bus.Mask : number, null);
        }
    }
}
