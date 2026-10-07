using System.Collections.Generic;
using Exuarch.Core;

namespace Exuarch.Web.Components
{
    // Colours used from code, for inline styles and SVG attributes. They match the --cat-* tokens in app.css.
    public static class Palette
    {
        public static readonly string[] Buses = { "#3d6fd8", "#0c8599", "#7c5cc4", "#b7791f", "#2b8a3e", "#c2255c" };
        public const string Unknown = "#a9adb4";
        public const string Error = "#c92a2a";

        private static readonly Dictionary<string, string> Categories = new Dictionary<string, string>
        {
            ["Registers"] = "#3d6fd8",
            ["Control"] = "#7c5cc4",
            ["Arithmetic"] = "#b7791f",
            ["Memory"] = "#2b8a3e",
            ["I/O"] = "#c2255c",
        };

        public static string Category(string category)
        {
            return category != null && Categories.TryGetValue(category, out var color) ? color : Unknown;
        }
        public static string Category(DeviceTypeInfo info)
        {
            return Category(info?.Category);
        }
        public static string Bus(int index)
        {
            return index < 0 ? Unknown : Buses[index % Buses.Length];
        }
        // A bus of the machine, in the colour of its place among the machine's buses.
        public static string Bus(MachineDefinition definition, string busId)
        {
            return Bus(definition.Buses.FindIndex(b => b.Id == busId));
        }
    }
}
