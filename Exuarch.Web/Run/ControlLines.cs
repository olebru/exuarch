using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Run
{
    // A device's control lines, in the colour of its category. The control strip lights them and the decoder ROM has a
    // column for each.
    public sealed record ControlLineGroup(DeviceDefinition Device, DeviceTypeInfo Info, string Color, List<string> Lines)
    {
        // Every control line of the machine, grouped by device, in definition order.
        public static List<ControlLineGroup> Of(RunSession session)
        {
            var groups = new List<ControlLineGroup>();
            foreach (var device in session.Machine.Definition.Devices)
            {
                var info = session.Info(device);
                var lines = SignalResolver.LinesOf(info, session.Machine.Device(device.Id)) ?? new List<string>();
                if (lines.Count > 0) groups.Add(new ControlLineGroup(device, info, Palette.Category(info), lines));
            }
            return groups;
        }

        // The group's lights in the control strip: every line, or only those on in the last tick.
        public List<LineLight> Lights(LastTickView last, bool onlyOn)
        {
            var shown = onlyOn ? Lines.Where(line => last.LineActive(Device.Id, line)) : Lines;
            return shown.Select(line => new LineLight(line, last.LineActive(Device.Id, line) ? "led on" : "led ", Title(line))).ToList();
        }

        // The line by its full name, and what it does.
        private string Title(string line)
        {
            var description = Info.ControlLines.FirstOrDefault(l => l.Name == line)?.Description;
            return $"{Device.Id}.{line}{(string.IsNullOrEmpty(description) ? "" : ": " + description)}";
        }
    }

    public sealed record LineLight(string Line, string Css, string Title);
}
