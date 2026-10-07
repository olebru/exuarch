namespace Exuarch.Web.Components
{
    // Counts in words: "1 problem", "3 problems", "2 buses".
    public static class Words
    {
        // What a noun takes for a count: nothing for one, otherwise its plural ending.
        public static string Plural(int n, string ending = "s") => n == 1 ? "" : ending;

        public static string Count(int n, string noun) => $"{n} {noun}{Plural(n)}";
    }
}
