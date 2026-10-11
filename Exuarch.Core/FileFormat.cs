using System.IO;
using System.Text;
using System.Text.Json;
namespace Exuarch.Core
{
    public static class FileFormat
    {
        public const int First = 1;
        public const int Current = 1;
        private const string VersionProperty = "formatVersion";

        public static void RejectUnreadable(int? version, string what)
        {
            int read = version ?? First;
            if (read > Current)
                throw new MachineDefinitionException($"{what} uses file format {read}, from a newer ExµArch. This ExµArch reads file formats up to {Current}: update it to open the file.");
            if (read < First)
                throw new MachineDefinitionException($"{what} has formatVersion {read}. File formats start at {First}.");
        }

        public static string WithCurrentVersion(string objectJson)
        {
            using var document = JsonDocument.Parse(objectJson);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber(VersionProperty, Current);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name != VersionProperty) property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return CompactJson.Format(Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
}
