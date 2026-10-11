namespace Exuarch.Core
{
    internal static class Words
    {
        public static string Plural(int count) { return count == 1 ? "" : "s"; }

        public static string Count(int count, string noun) { return $"{count} {noun}{Plural(count)}"; }
    }
}
