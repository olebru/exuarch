using System.Linq;
using System.Text;
using System.Text.Json;
namespace Exuarch.Core
{
    internal static class CompactJson
    {
        private const int MaxInlineLength = 100;
        private const int MaxInlineProperties = 4;

        public static string Format(string json)
        {
            using var document = JsonDocument.Parse(json);
            var builder = new StringBuilder();
            Write(builder, document.RootElement, 0);
            return builder.ToString();
        }

        private static void Write(StringBuilder builder, JsonElement element, int depth)
        {
            var inline = Inline(element);
            if (inline != null && (!IsContainer(element) || inline.Length + depth * 2 <= MaxInlineLength))
            {
                builder.Append(inline);
                return;
            }
            var indent = new string(' ', (depth + 1) * 2);
            if (element.ValueKind == JsonValueKind.Array)
            {
                builder.Append('[');
                bool first = true;
                foreach (var item in element.EnumerateArray())
                {
                    builder.Append(first ? "\n" : ",\n").Append(indent);
                    Write(builder, item, depth + 1);
                    first = false;
                }
                builder.Append('\n').Append(' ', depth * 2).Append(']');
            }
            else
            {
                builder.Append('{');
                bool first = true;
                foreach (var property in element.EnumerateObject())
                {
                    builder.Append(first ? "\n" : ",\n").Append(indent);
                    builder.Append(JsonSerializer.Serialize(property.Name, MachineDefinitionJsonContext.Default.String)).Append(": ");
                    Write(builder, property.Value, depth + 1);
                    first = false;
                }
                builder.Append('\n').Append(' ', depth * 2).Append('}');
            }
        }

        private static string Inline(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    var items = element.EnumerateArray().ToList();
                    if (items.Any(IsContainer)) return null;
                    return items.Count == 0 ? "[]" : "[" + string.Join(", ", items.Select(i => i.GetRawText())) + "]";
                case JsonValueKind.Object:
                    var properties = element.EnumerateObject().ToList();
                    if (properties.Count > MaxInlineProperties || properties.Any(p => IsContainer(p.Value))) return null;
                    return properties.Count == 0 ? "{}" : "{ " + string.Join(", ", properties.Select(p =>
                        JsonSerializer.Serialize(p.Name, MachineDefinitionJsonContext.Default.String) + ": " + p.Value.GetRawText())) + " }";
                default:
                    return element.GetRawText();
            }
        }
        private static bool IsContainer(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Array || element.ValueKind == JsonValueKind.Object;
        }
    }
}
