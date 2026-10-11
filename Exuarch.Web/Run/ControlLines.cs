using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Run
{
    public sealed record ControlLineGroup(DeviceDefinition Device, DeviceTypeInfo Info, string Color, List<string> Lines)
    {
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

        public List<LineLight> Lights(LastTickView last)
        {
            return Lines.Select(line => new LineLight(line, last.LineActive(Device.Id, line) ? "led on" : "led ", Title(line))).ToList();
        }

        private string Title(string line)
        {
            var description = Info.ControlLines.FirstOrDefault(l => l.Name == line)?.Description;
            return $"{Device.Id}.{line}{(string.IsNullOrEmpty(description) ? "" : ": " + description)}";
        }
    }

    public sealed record LineLight(string Line, string Css, string Title);
}
