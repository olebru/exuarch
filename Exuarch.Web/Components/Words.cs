namespace Exuarch.Web.Components
{
    public static class Words
    {
        public static string Plural(int n, string ending = "s") => n == 1 ? "" : ending;

        public static string Count(int n, string noun) => $"{n} {noun}{Plural(n)}";
    }
}
