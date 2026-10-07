namespace Exuarch.Core
{
    // Counts in the messages: "1 operand", "3 operands".
    internal static class Words
    {
        // What a noun takes for a count: nothing for one, otherwise an s.
        public static string Plural(int count) { return count == 1 ? "" : "s"; }

        public static string Count(int count, string noun) { return $"{count} {noun}{Plural(count)}"; }
    }
}
