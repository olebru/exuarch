using System.IO;
using Exuarch.Core;

namespace Exuarch.Web.Workbench
{
    public static class PackageFile
    {
        public static MachinePackage Read(string json, string fileName)
        {
            var package = MachinePackage.FromJson(json);
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = package.Machine.Name;
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = Path.GetFileNameWithoutExtension(fileName);
            return package;
        }
    }
}
