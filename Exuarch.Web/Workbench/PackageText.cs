using System.Collections.Generic;
using Exuarch.Core;

namespace Exuarch.Web.Workbench
{
    public static class PackageText
    {
        public static string Of(MachinePackage package)
        {
            if (package == null) return "";
            var copy = package.Clone();
            copy.Machine.EnsureLayout();
            var sections = new List<string>();
            if (!string.IsNullOrEmpty(copy.Readme)) sections.Add(Section("README", copy.Readme));
            copy.Readme = null;
            foreach (var program in copy.Programs)
            {
                sections.Add(Section($"Program: {program.Name}", program.Source));
                program.Source = null;
            }
            sections.Insert(0, copy.ToJson());
            return string.Join("\n\n", sections);
        }

        private static string Section(string heading, string text) => $"── {heading} ──\n{text}";
    }
}
