using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Exuarch.Web.Run
{
    // A panel of the Run view, which can go in either column or among the tabs below: what its head says, where it
    // starts out, and the components that draw its body and, if it has them, its tools and the line by its title.
    public sealed class RunPanel
    {
        public string Id { get; init; }
        public string Title { get; init; }
        // A second, quieter part of the title: which device a screen or LCD is.
        public string Detail { get; init; }
        public string Css { get; init; } = "";
        // The body has its own scroll bar in the side column, rather than growing the column.
        public bool Scrolls { get; init; }
        public (string Target, string Title)? Help { get; init; }
        public Dock DefaultDock { get; init; } = Dock.Left;
        // As a tab below, the body gets padding; panels that bring their own do not.
        public bool Padded { get; init; } = true;
        // The device a screen, LCD or keypad panel shows.
        public IBusDevice Device { get; init; }
        public RunSession Session { get; init; }

        // Components that take the session and the panel (see RunPanelPart).
        public PanelPart Body { get; init; }
        public PanelPart Tools { get; init; }
        // Next to the title, and over the body as a tab below, with HeadCss on the head.
        public PanelPart Head { get; init; }
        public string HeadCss { get; init; } = "";
        // Next to the title only while the panel is folded away.
        public PanelPart FoldedHead { get; init; }

        // What goes next to the title.
        public PanelPart HeadShown(bool folded) => folded ? FoldedHead ?? Head : Head;

        // A part of this panel, to put in the markup.
        public RenderFragment Show(PanelPart part) => builder => part.Render(builder, this);
    }

    // The component that draws a part of a panel, given the session and the panel. Naming the component's type here,
    // rather than handing a Type to a DynamicComponent, keeps it and the parameters it is given in a trimmed build.
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

    // A part of a panel, its body, tools or head.
    public abstract class RunPanelPart : ComponentBase
    {
        [Parameter] public RunSession Session { get; set; }
        [Parameter] public RunPanel Panel { get; set; }

        protected Machine Machine => Session.Machine;
    }

    public static class RunPanels
    {
        // The machine's input and output, by kind: every screen first, then every LCD, then every keypad.
        private static readonly (Type Kind, string Title, PanelPart Body)[] DevicePanels =
        {
            (typeof(IScreen), "Screen", PanelPart.Of<ScreenPanel>()),
            (typeof(CharacterDisplay), "LCD", PanelPart.Of<LcdPanel>()),
            (typeof(Keypad), "Keypad", PanelPart.Of<KeypadPanel>()),
        };

        // Every panel the session's machine has, in the order they start out in: the side columns' first, then the
        // tabs'. The machine's input and output start on the right, the memory, decoder ROM and trace below, and how the
        // program runs, the clock, the listing and what is executing, on the left.
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
