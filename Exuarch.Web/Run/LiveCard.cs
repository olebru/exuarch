using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Exuarch.Web.Components;
using Exuarch.Web.Devices;

namespace Exuarch.Web.Run
{
    // A device's card on the live drawing: lit while its control lines are on, while it drives a bus or reads one, and
    // flashing when its value changed; an ALU shows the result it drives.
    public sealed record LiveCard(DeviceDefinition Device, string Kind, string HeadTitle, string Color, string Css, IBusDevice Built,
        IReadOnlyList<string> Signals, int? AluResult, bool Changed, IReadOnlyList<CardSocket> Sockets)
    {
        public static IEnumerable<LiveCard> Of(RunSession session)
        {
            var last = session.Last;
            return session.Machine.Definition.Devices.Where(d => d.Layout != null).Select(device => Of(session, last, device));
        }

        private static LiveCard Of(RunSession session, LastTickView last, DeviceDefinition device)
        {
            var info = session.Info(device);
            var driving = device.Ports().Where(p => last.Drives(device.Id, p.Value)).Select(p => p.Value).FirstOrDefault();
            bool reading = device.Ports().Any(p => last.Reads(device.Id, p.Value));
            return new LiveCard(
                device,
                device.Name ?? device.Type,
                $"{device.Type}: {info.Description}",
                Palette.Category(info),
                $"{(last.IsActive(device.Id) ? "active" : "")} {(driving != null ? "driving" : "")} {(reading ? "reading" : "")}",
                session.Machine.Device(device.Id),
                last.SignalsOf(device.Id).ToList(),
                driving != null ? last.TransferOn(driving).Value : null,
                last.Changed(device.Id),
                DeviceCard.SocketsOf(device, info));
        }
    }
}
