using System.Reflection;

namespace Exuarch.Web
{
    public static class BuildInfo
    {
        public static readonly string Version;
        public static readonly string Commit;

        static BuildInfo()
        {
            var informational = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            var plus = informational.IndexOf('+');
            Version = plus < 0 ? informational : informational.Substring(0, plus);
            var commit = plus < 0 ? "" : informational.Substring(plus + 1);
            Commit = commit.Length > 7 ? commit.Substring(0, 7) : commit;
        }

        public static bool IsLocal => Version == "" || Version.StartsWith("0.0.0");
        public static string Label => IsLocal ? "local build" : $"v{Version}";
    }
}
