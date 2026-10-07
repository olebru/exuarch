using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Exuarch.Core;
using Exuarch.Web.Workbench;
using Microsoft.Extensions.DependencyInjection;


namespace Exuarch.Web.Tests;

// The page as people see it: every built in package opened, every tab and the getting started drawer, devices and
// buses selected, instructions picked, programs loaded and the machine stepped. The markup must stay as it is, with
// two exceptions that do not change what is shown: the attributes Blazor's CSS isolation adds (b-xxxxxxxxxx) are all
// written the same, and the comments that mark where components start and Blazor's own blazor:* attributes (event
// handler and element reference ids) are left out, as are the real milliseconds a real time clock card shows.
public class AppSnapshotTests : BunitContext
{
    public AppSnapshotTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        // What the browser saved is never answered, so the page does not restore or save anything: saving happens on a
        // timer, half a second after a change, and would re-render the page at moments the test does not choose.
        JSInterop.Setup<string>("exuarchStore.get", _ => true);
        // A returning visitor: the splash screen stays away.
        JSInterop.Setup<bool>("exuarchWelcome.seen").SetResult(true);
        // The design canvas at the top left of the page.
        JSInterop.Setup<Exuarch.Web.Components.ElementRect>("exuarchEditor.rect", _ => true).SetResult(new Exuarch.Web.Components.ElementRect { Width = 1200, Height = 800 });
        // Monaco draws the program editor with JavaScript; here it stays an empty element.
        ComponentFactories.Add<BlazorMonaco.Editor.StandaloneCodeEditor>(() => new InertEditor());
        // The screen is drawn through [JSImport], which only runs in the browser.
        ComponentFactories.AddStub<Exuarch.Web.Devices.FramebufferView>();
        Services.AddScoped<HelpService>();
        Services.AddScoped<Analytics>();
        // Real time clocks read a clock that stands still, so a slow test run can not make one beat.
        Services.AddSingleton(DeviceRegistry.CreateDefault(new StoppedTime()));
    }

    public static TheoryData<string> Packages()
    {
        var data = new TheoryData<string>();
        foreach (var package in BuiltInPackages.All) data.Add(package.Name);
        return data;
    }

    private IRenderedComponent<ComputerSIM> Open(string package)
    {
        var cut = Render<ComputerSIM>();
        cut.Find(".package-name").Click();
        foreach (var toggle in cut.FindAll(".level-toggle.collapsed").ToList()) cut.FindAll(".level-toggle.collapsed").First().Click();
        var example = cut.FindAll(".example").First(e => e.QuerySelector(".example-name")?.TextContent.Trim() == package);
        var load = example.QuerySelectorAll("button").FirstOrDefault(b => b.TextContent.Trim() == "Load");
        load?.Click();
        cut.Find(".drawer-close").Click();
        Assert.Contains(package, cut.Find(".package-name").TextContent);
        return cut;
    }

    private static void Tab(IRenderedComponent<ComputerSIM> cut, string name)
    {
        cut.FindAll(".nav-tabs a").First(a => a.TextContent.Contains(name)).Click();
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void HardwareDesign(string package)
    {
        var cut = Open(package);
        var text = new StringBuilder();
        text.Append(Section("page", cut.Markup));
        var definition = BuiltInPackages.Get(package).Machine;
        foreach (var device in definition.Devices)
        {
            var card = cut.FindAll(".canvas .dev").FirstOrDefault(d => d.QuerySelector(".dev-id")?.TextContent.Trim() == device.Id);
            if (card == null) { text.Append($"== no card for {device.Id}\n"); continue; }
            card.PointerDown();
            cut.Find(".editor").PointerUp();
            text.Append(Section($"selected {device.Id}", cut.Find(".inspector").OuterHtml));
        }
        foreach (var bus in cut.FindAll("g.bus").Select((g, i) => i).ToList())
        {
            cut.FindAll("g.bus")[bus].PointerDown();
            cut.Find(".editor").PointerUp();
            text.Append(Section($"bus {bus}", cut.Find(".inspector").OuterHtml));
        }
        var decoder = cut.FindAll(".canvas .dev").FirstOrDefault(d => d.QuerySelector(".dev-id")?.TextContent.Trim() == "decoder");
        if (decoder != null)
        {
            decoder.PointerDown();
            cut.Find(".editor").PointerUp();
            text.Append(Section("decoder", cut.Find(".inspector").OuterHtml));
        }
        Snapshot.Match($"hardware-{package}", text.ToString());
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Microcode(string package)
    {
        var cut = Open(package);
        Tab(cut, "Microcode");
        var text = new StringBuilder();
        text.Append(Section("page", cut.Markup));
        int count = cut.FindAll(".list-item").Count;
        for (int i = 0; i < count; i += Math.Max(1, count / 8))
        {
            cut.FindAll(".list-item")[i].Click();
            text.Append(Section($"instruction {i}", cut.Find(".mc-editor .body").OuterHtml));
        }
        Snapshot.Match($"microcode-{package}", text.ToString());
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void ProgramAndRun(string package)
    {
        var cut = Open(package);
        var text = new StringBuilder();
        foreach (var name in BuiltInPackages.Get(package).Programs.Select(p => p.Name))
        {
            Tab(cut, "Program");
            cut.FindAll(".examples button").First(b => b.TextContent.Trim() == name).Click();
            var confirm = cut.FindAll(".confirm-bar .btn-danger").FirstOrDefault();
            confirm?.Click();
            text.Append(Section($"program {name}", cut.Find(".workspace").OuterHtml));
            Tab(cut, "Run");
            text.Append(Section($"run {name}", cut.Find(".run").OuterHtml));
            for (int i = 0; i < 5; i++) Control(cut, "Tick").Click();
            text.Append(Section($"run {name} after 5 ticks", cut.Find(".run").OuterHtml));
            for (int i = 0; i < 3; i++) Control(cut, "Instruction").Click();
            text.Append(Section($"run {name} after 3 instructions", cut.Find(".run").OuterHtml));
            Control(cut, "Reset").Click();
        }
        Snapshot.Match($"program-run-{package}", text.ToString());
    }

    private sealed class StoppedTime : TimeProvider
    {
        public override long GetTimestamp() => 0;
        public override long TimestampFrequency => 1000;
    }

    private sealed class InertEditor : BlazorMonaco.Editor.StandaloneCodeEditor
    {
        protected override System.Threading.Tasks.Task OnAfterRenderAsync(bool firstRender) => System.Threading.Tasks.Task.CompletedTask;
    }

    private static IElement Control(IRenderedComponent<ComputerSIM> cut, string label)
    {
        return cut.FindAll(".run .controls button").First(b => b.TextContent.Contains(label));
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Drawer(string package)
    {
        var cut = Open(package);
        var text = new StringBuilder();
        cut.Find(".package-name").Click();
        foreach (var section in new[] { "Handbook", "Machines", "This machine" })
        {
            cut.FindAll(".drawer-section").First(b => b.TextContent.Trim() == section).Click();
            text.Append(Section(section, cut.Find(".drawer").OuterHtml));
        }
        var json = cut.FindAll(".drawer button").FirstOrDefault(b => b.TextContent.Trim() == "JSON");
        if (json != null)
        {
            json.Click();
            text.Append(Section("This machine JSON", cut.Find(".drawer").OuterHtml));
        }
        Snapshot.Match($"drawer-{package}", text.ToString());
    }

    [Fact]
    public void Handbook()
    {
        var cut = Open(BuiltInPackages.Default.Name);
        var text = new StringBuilder();
        cut.Find(".package-name").Click();
        cut.FindAll(".drawer-section").First(b => b.TextContent.Trim() == "Handbook").Click();
        text.Append(Section("contents", cut.Find(".drawer").OuterHtml));
        foreach (var query in new[] { "alu", "interrupt", "zzzz" })
        {
            cut.Find(".handbook-search input").Input(query);
            text.Append(Section($"search {query}", cut.Find(".drawer").OuterHtml));
        }
        Snapshot.Match("handbook", text.ToString());
    }

    private static string Section(string title, string markup)
    {
        return $"== {title}\n{Normalize(markup)}\n";
    }

    private static readonly Regex Scope = new Regex(@" b-[a-z0-9]{10}(=""[^""]*"")?", RegexOptions.Compiled);
    private static readonly Regex BlazorAttribute = new Regex(@" blazor:[A-Za-z:]+=""[^""]*""", RegexOptions.Compiled);
    // A real time clock card shows the milliseconds of real time since its last beat.
    private static readonly Regex RealTime = new Regex(@">\d+ / (\d+) ms<", RegexOptions.Compiled);
    private static readonly Regex Marker = new Regex(@"<!--!-->", RegexOptions.Compiled);
    private static readonly Regex Between = new Regex(@">\s*<", RegexOptions.Compiled);

    private static string Normalize(string markup)
    {
        markup = Marker.Replace(markup, "");
        markup = BlazorAttribute.Replace(markup, "");
        markup = RealTime.Replace(markup, ">… / $1 ms<");
        markup = Scope.Replace(markup, " b-scope");
        return Between.Replace(markup, ">\n<");
    }
}
