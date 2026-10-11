using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Exuarch.Web.Run
{
    public sealed class RunPanel
    {
        public string Id { get; init; }
        public string Title { get; init; }
        public string Detail { get; init; }
        public string Css { get; init; } = "";
        public bool Scrolls { get; init; }
        public (string Target, string Title)? Help { get; init; }
        public Dock DefaultDock { get; init; } = Dock.Left;
        public bool Padded { get; init; } = true;
        public IBusDevice Device { get; init; }
        public RunSession Session { get; init; }

        public PanelPart Body { get; init; }
        public PanelPart Tools { get; init; }
        public PanelPart Head { get; init; }
        public string HeadCss { get; init; } = "";
        public PanelPart FoldedHead { get; init; }

        public PanelPart HeadShown(bool folded) => folded ? FoldedHead ?? Head : Head;

        public RenderFragment Show(PanelPart part) => builder => part.Render(builder, this);
    }

    public sealed class PanelPart
    {
        private PanelPart(Action<RenderTreeBuilder, RunPanel> render)
        {
            Render = render;
        }

        public Action<RenderTreeBuilder, RunPanel> Render { get; }

        public static PanelPart Of<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : RunPanelPart
        {
            return new PanelPart(Draw<T>);
        }

        private static void Draw<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(RenderTreeBuilder builder, RunPanel panel) where T : RunPanelPart
        {
            builder.OpenComponent<T>(0);
            builder.AddComponentParameter(1, nameof(RunPanelPart.Session), panel.Session);
            builder.AddComponentParameter(2, nameof(RunPanelPart.Panel), panel);
            builder.CloseComponent();
        }
    }

    public abstract class RunPanelPart : ComponentBase
    {
        [Parameter] public RunSession Session { get; set; }
        [Parameter] public RunPanel Panel { get; set; }

        protected Machine Machine => Session.Machine;
    }

    public static class RunPanels
    {
        private static readonly (Type Kind, string Title, PanelPart Body)[] DevicePanels =
        {
            (typeof(IScreen), "Screen", PanelPart.Of<ScreenPanel>()),
            (typeof(CharacterDisplay), "LCD", PanelPart.Of<LcdPanel>()),
            (typeof(SegmentDisplay), "Display", PanelPart.Of<SegmentPanel>()),
            (typeof(Keypad), "Keypad", PanelPart.Of<KeypadPanel>()),
        };

        public static List<RunPanel> For(RunSession session)
        {
            var panels = new List<RunPanel> { new RunPanel { Id = "clock", Title = "Clock speed", Css = "clock-panel", Body = PanelPart.Of<ClockChart>(), Head = PanelPart.Of<ClockHead>(), HeadCss = "clock-head", Session = session } };
            panels.AddRange(DevicePanels.SelectMany(kind => session.Machine.Devices.Where(kind.Kind.IsInstanceOfType).Select(device => new RunPanel
            {
                Id = "device:" + device.ID(), Title = kind.Title, Detail = device.ID(), Css = "display-panel", DefaultDock = Dock.Right, Body = kind.Body, Device = device, Session = session,
            })));
            panels.Add(new RunPanel { Id = "program", Title = "Program", Css = "program", Scrolls = true, Padded = false, Body = PanelPart.Of<ProgramListing>(), Session = session });
            panels.Add(new RunPanel { Id = "now", Title = "Now executing", Css = "now", Body = PanelPart.Of<NowExecuting>(), FoldedHead = PanelPart.Of<NowInline>(), Session = session });
            panels.Add(new RunPanel
            {
                Id = "memory", Title = "Memory", Css = "memory-panel", Scrolls = true, Help = ("memory-and-banks", "Memory and banks"), DefaultDock = Dock.Bottom, Padded = false,
                Body = PanelPart.Of<MemoryDump>(), Tools = PanelPart.Of<MemoryTools>(), Session = session,
            });
            panels.Add(new RunPanel
            {
                Id = "decoder", Title = "Decoder ROM", Css = "decoder-panel", Scrolls = true, Help = ("flags-and-conditions", "The decoder ROM, flags and conditions"), DefaultDock = Dock.Bottom, Padded = false,
                Body = PanelPart.Of<DecoderRomPanel>(), Tools = PanelPart.Of<DecoderTools>(), Session = session,
            });
            panels.Add(new RunPanel
            {
                Id = "trace", Title = "Trace", Css = "trace-panel", Scrolls = true, Help = ("buses-and-ticks", "Buses and the two-phase tick"), DefaultDock = Dock.Bottom, Padded = false,
                Body = PanelPart.Of<TracePanel>(), Session = session,
            });
            return panels;
        }
    }
}
