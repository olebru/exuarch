using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Exuarch.Web.Devices
{
    public record CardSocket(string Name, string Label, string Target, string Missing, string Description);

    public partial class DeviceCard
    {
        [Parameter] public DeviceDefinition Device { get; set; }
        [Parameter] public SchematicLayout Layout { get; set; }
        [Parameter] public string Id { get; set; }
        [Parameter] public string Kind { get; set; }
        [Parameter] public string HeadTitle { get; set; }
        [Parameter] public string CardTitle { get; set; }
        [Parameter] public double Left { get; set; }
        [Parameter] public double Top { get; set; }
        [Parameter] public double Height { get; set; } = SchematicLayout.CardMinHeight;
        [Parameter] public string Color { get; set; }
        [Parameter] public string CssClass { get; set; }
        [Parameter] public Func<string, string> BusColor { get; set; }

        [Parameter] public IBusDevice Built { get; set; }
        [Parameter] public Machine Machine { get; set; }
        [Parameter] public IReadOnlyList<string> Signals { get; set; } = Array.Empty<string>();
        [Parameter] public int? AluResult { get; set; }
        [Parameter] public bool Changed { get; set; }
        [Parameter] public long ChangeKey { get; set; }
        [Parameter] public IReadOnlyList<CardSocket> Sockets { get; set; } = Array.Empty<CardSocket>();
        [Parameter] public string Line { get; set; }
        [Parameter] public string LineValue { get; set; }
        [Parameter] public string SubLine { get; set; }

        [Parameter] public EventCallback<PointerEventArgs> OnCardPointerDown { get; set; }
        [Parameter] public EventCallback<(PointerEventArgs, string)> OnSocketPointerDown { get; set; }
        [Parameter] public EventCallback<(PointerEventArgs, string)> OnPortPointerDown { get; set; }

        private bool Interactive => OnCardPointerDown.HasDelegate;

        public static IReadOnlyList<CardSocket> SocketsOf(DeviceDefinition device, DeviceTypeInfo info)
        {
            return info.Connections.Select(c => new CardSocket(c.Name, c.Name, device.Connections.TryGetValue(c.Name, out var target) ? target : null, "drag to a device", c.Description)).ToList();
        }
        public static IReadOnlyList<CardSocket> DecoderSocketsOf(SchematicLayout layout)
        {
            return SchematicLayout.DecoderSockets.Select(s => new CardSocket(s.Name, s.Label, layout.DecoderTarget(s.Name), s.Name == "interrupts" ? "none" : "drag to a device", s.Description)).ToList();
        }
    }
}
