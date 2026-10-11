using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Microsoft.JSInterop;

namespace Exuarch.Web
{
    public class Analytics : IAnalyticsSink
    {
        private readonly IJSRuntime js;
        private readonly HashSet<string> sent = new HashSet<string>();

        public Analytics(IJSRuntime js)
        {
            this.js = js;
        }

        public static readonly string[] Milestones = { "explored", "tutorial", "edited hardware", "edited microcode", "own program ran", "built a machine" };
        private int furthest = -1;

        public static string MachineName(string name) => name != null && Workspace.IsBuiltIn(name) ? name : "own";

        public class View
        {
            public string Tab;
            public string Machine;
            public bool MachineIsBuiltIn;
            public bool ProgramShips;
            public (string Kind, string Target)? Page;
            public string Errors;
            public bool Active;
        }

        private View view = new View();
        private readonly IViewObserver[] observers = { new FirstUseObserver(), new PageReadObserver(), new StuckObserver() };

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
            foreach (var observer in observers) await observer.Observe(now, time, this);
        }

        public Task EditedHardware() => Milestone("edited hardware", ("machine", MachineName(view.Machine)));
        public Task EditedMicrocode() => Milestone("edited microcode", ("machine", MachineName(view.Machine)));

        public async Task Halted()
        {
            if (view.ProgramShips) await Milestone("explored", ("machine", MachineName(view.Machine)));
            else
            {
                await Milestone("own program ran", ("machine", MachineName(view.Machine)));
                if (!view.MachineIsBuiltIn) await Milestone("built a machine");
            }
        }

        public async Task Ran(bool max, double seconds, double ticksPerSecond)
        {
            if (sent.Add("run:" + MachineName(view.Machine))) await Send("run", ("machine", MachineName(view.Machine)), ("speed", max ? "max" : "set"));
            if (seconds >= 10 && view.ProgramShips) await Milestone("explored", ("machine", MachineName(view.Machine)));
            if (max && seconds >= 3) await Summary("top speed", SpeedBucket(ticksPerSecond), keepHighest: true);
        }

        public Task RuntimeError(string message)
        {
            var kind = message.Contains("blue smoke") ? "bus error" : "runtime error";
            return sent.Add($"{kind}:{MachineName(view.Machine)}") ? Send(kind, ("machine", MachineName(view.Machine))) : Task.CompletedTask;
        }

        public Task Splash(string choice) => Send("splash", ("choice", Workspace.IsBuiltIn(choice) || choice is "guide" or "series" or "closed" ? choice : "closed"));
        public Task NewMachine(string start) => Send("new machine", ("start", start is "empty" or "copy" ? start : "minimal"));
        public Task Import() => Send("import");
        public Task Export(string name) => Send("export", ("machine", MachineName(name)));

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

        bool IAnalyticsSink.Once(string key) => sent.Add(key);
        Task IAnalyticsSink.Send(string name, params (string Key, string Value)[] data) => Send(name, data);
        Task IAnalyticsSink.Milestone(string name, string once, params (string Key, string Value)[] data) => Milestone(name, once, data);
        Task IAnalyticsSink.Summary(string key, string value) => Summary(key, value);

        private async Task Call(string function, params object[] args)
        {
            try { await js.InvokeVoidAsync(function, args); }
            catch (JSException) { }
            catch (InvalidOperationException) { }
            catch (TaskCanceledException) { }
        }
    }
}
