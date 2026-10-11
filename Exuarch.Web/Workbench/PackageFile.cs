using System.IO;
using System.Text.Json;
using Exuarch.Core;

namespace Exuarch.Web.Workbench
{
    public static class PackageFile
    {
        public static MachinePackage Read(string json, string fileName)
        {
            var package = IsMachineFile(json)
                ? new MachinePackage { Machine = MachineDefinition.FromJson(json) }
                : MachinePackage.FromJson(json);
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = package.Machine.Name;
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = Path.GetFileNameWithoutExtension(fileName).Replace(".machine", "");
            return package;
        }

        private static bool IsMachineFile(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = document.RootElement;
                return root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("machine", out _)
                    && (root.TryGetProperty("buses", out _) || root.TryGetProperty("devices", out _));
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
