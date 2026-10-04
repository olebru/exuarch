using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Microsoft.JSInterop;

namespace Exuarch.Web
{
    // What people manage to do in the app, counted anonymously (see wwwroot/js/analytics.js). Instead of every click it
    // sends milestones, the first time in a visit that someone gets somewhere, a few outcomes that show where people get
    // stuck, and one summary of the visit when the page closes. Umami's funnel and goal reports work on the milestones.
    //
    // Only the values listed here are sent: names of built in machines and handbook pages, and coarse buckets. A machine
    // of your own is "own", and programs, microcode and error messages never leave the browser. Nothing is stored in the
    // browser either, so "once" means once per visit. Umami bills every property as an event, so each has one or two.
    public class Analytics
    {
        private readonly IJSRuntime js;
        private readonly HashSet<string> sent = new HashSet<string>();

        public Analytics(IJSRuntime js)
        {
            this.js = js;
        }

        // The milestones in the order a newcomer reaches them; the summary reports the furthest one.
        public static readonly string[] Milestones = { "explored", "tutorial", "edited hardware", "edited microcode", "own program ran", "built a machine" };
        private int furthest = -1;

        // A built in package by its name, anything else as "own".
        public static string MachineName(string name) => name != null && Workspace.IsBuiltIn(name) ? name : "own";

        // ---- What the page tells it after every render, and every few seconds ----

        public class View
        {
            public string Tab;
            public string Machine;
            public bool MachineIsBuiltIn;
            // The program in the editor is one that ships with the built in machine, unchanged.
            public bool ProgramShips;
            // The open handbook page, if the drawer shows one.
            public (string Kind, string Target)? Page;
            // Where the machine has errors now: "hardware", "microcode" or "program", or null.
            public string Errors;
            // True when the person did something in the last minute: a key press, click, scroll or typing anywhere.
            public bool Active;
        }

        private View view = new View();
        private readonly HashSet<string> tabs = new HashSet<string>();
        private (string Kind, string Target)? page;
        private DateTime pageSince;
        private string errors;
        private double errorSeconds;
        private DateTime lastObserved;
        private readonly HashSet<string> stuck = new HashSet<string>();

        public async Task<double> IdleSeconds()
        {
            try { return await js.InvokeAsync<double>("exuarchAnalytics.idleSeconds"); }
            catch (JSException) { return 0; }
            catch (InvalidOperationException) { return 0; }
        }

        public async Task Observe(View now)
        {
            var time = DateTime.UtcNow;
            view = now;
            if (now.Machine != null && sent.Add("machine:" + MachineName(now.Machine))) await Send("machine", ("machine", MachineName(now.Machine)));
            if (now.Tab != null && tabs.Add(now.Tab)) await Summary("tabs used", tabs.Count.ToString());

            // A page counts as read after 30 seconds open: a tutorial is a milestone, any other page a read.
            if (now.Page != page) { page = now.Page; pageSince = time; }
            if (page is { } open && (time - pageSince).TotalSeconds >= 30)
            {
                var guide = open.Kind == "guide" ? Guides.Find(open.Target) : null;
                // Each tutorial once, so the counts show how far through the series people get.
                if (guide?.Section == "Tutorials") await Milestone("tutorial", "tutorial:" + guide.Id, ("tutorial", guide.Id));
                else if (guide != null || (open.Kind == "reference" && DeviceReference.Exists(open.Target)))
                {
                    if (sent.Add($"read:{open.Kind}/{open.Target}")) await Send("read", ("page", $"{open.Kind}/{open.Target}"));
                }
            }

            // Errors that stay for two minutes while the person is busy with the app, not while the tab sits idle.
            bool busy = now.Active;
            if (now.Errors != errors)
            {
                if (errors != null && now.Errors == null)
                {
                    foreach (var where in stuck.ToList()) await Send("unstuck", ("where", where));
                    stuck.Clear();
                }
                errors = now.Errors;
                errorSeconds = 0;
            }
            else if (errors != null && busy && lastObserved != default)
            {
                errorSeconds += Math.Min(10, (time - lastObserved).TotalSeconds);
                if (errorSeconds >= 120 && stuck.Add(errors)) await Send("stuck", ("where", errors), ("machine", MachineName(now.Machine)));
            }
            lastObserved = time;
        }

        // ---- What the editors and the Run view report ----

        public Task EditedHardware() => Milestone("edited hardware", ("machine", MachineName(view.Machine)));
        public Task EditedMicrocode() => Milestone("edited microcode", ("machine", MachineName(view.Machine)));

        // A run that reached HLT, however it got there: Run, Tick or Instruction.
        public async Task Halted()
        {
            if (view.ProgramShips) await Milestone("explored", ("machine", MachineName(view.Machine)));
            else
            {
                await Milestone("own program ran", ("machine", MachineName(view.Machine)));
                if (!view.MachineIsBuiltIn) await Milestone("built a machine");
            }
        }

        // A run with Run that went on for a while, halted or not. At max speed it also says how fast the simulator is.
        public async Task Ran(bool max, double seconds, double ticksPerSecond)
        {
            if (sent.Add("run:" + MachineName(view.Machine))) await Send("run", ("machine", MachineName(view.Machine)), ("speed", max ? "max" : "set"));
            if (seconds >= 10 && view.ProgramShips) await Milestone("explored", ("machine", MachineName(view.Machine)));
            if (max && seconds >= 3) await Summary("top speed", SpeedBucket(ticksPerSecond), keepHighest: true);
        }

        // The machine stopped with an error: two devices drove one bus, or something else went wrong.
        public Task RuntimeError(string message)
        {
            var kind = message.Contains("blue smoke") ? "bus error" : "runtime error";
            return sent.Add($"{kind}:{MachineName(view.Machine)}") ? Send(kind, ("machine", MachineName(view.Machine))) : Task.CompletedTask;
        }

        // What a first visitor picked on the splash screen: a built in machine to run, "guide" or "closed".
        public Task Splash(string choice) => Send("splash", ("choice", Workspace.IsBuiltIn(choice) || choice is "guide" or "closed" ? choice : "closed"));
        public Task NewMachine(string start) => Send("new machine", ("start", start is "empty" or "copy" ? start : "minimal"));
        public Task Import() => Send("import");
        public Task Export(string name) => Send("export", ("machine", MachineName(name)));

        // ---- Buckets ----

        public static readonly string[] SpeedBuckets = { "under 1k", "1k to 10k", "10k to 100k", "100k to 1M", "1M to 10M", "10M and more" };
        public static string SpeedBucket(double ticksPerSecond) => ticksPerSecond switch
        {
            < 1_000 => SpeedBuckets[0],
            < 10_000 => SpeedBuckets[1],
            < 100_000 => SpeedBuckets[2],
            < 1_000_000 => SpeedBuckets[3],
            < 10_000_000 => SpeedBuckets[4],
            _ => SpeedBuckets[5],
        };

        // ---- Sending ----

        private Task Milestone(string name, params (string Key, string Value)[] data) => Milestone(name, name, data);
        private async Task Milestone(string name, string once, params (string Key, string Value)[] data)
        {
            if (!sent.Add("milestone:" + once)) return;
            await Send(name, data);
            int index = Array.IndexOf(Milestones, name);
            if (index > furthest)
            {
                furthest = index;
                await Summary("furthest", name);
            }
        }

        private readonly Dictionary<string, string> summary = new Dictionary<string, string>();
        // The visit summary is kept in the page and sent from there when the page closes.
        private async Task Summary(string key, string value, bool keepHighest = false)
        {
            if (keepHighest && summary.TryGetValue(key, out var old) && Array.IndexOf(SpeedBuckets, old) >= Array.IndexOf(SpeedBuckets, value)) return;
            summary[key] = value;
            await Call("exuarchAnalytics.summary", key, value);
        }

        private Task Send(string name, params (string Key, string Value)[] data)
        {
            var properties = data.Length == 0 ? null : data.ToDictionary(d => d.Key, d => d.Value);
            return Call("exuarchAnalytics.track", name, properties);
        }

        private async Task Call(string function, params object[] args)
        {
            try { await js.InvokeVoidAsync(function, args); }
            catch (JSException) { }
            catch (InvalidOperationException) { }
            catch (TaskCanceledException) { }
        }
    }
}
