using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;

namespace Exuarch.Web
{
    // Where the analytics send what they work out (see Analytics): events, milestones and the visit summary.
    internal interface IAnalyticsSink
    {
        // True the first time in the visit it is asked about a key.
        bool Once(string key);
        Task Send(string name, params (string Key, string Value)[] data);
        Task Milestone(string name, string once, params (string Key, string Value)[] data);
        Task Summary(string key, string value);
    }

    // Something the analytics work out from what the page shows, which the page reports after every render and every
    // few seconds.
    internal interface IViewObserver
    {
        Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink);
    }

    // The first time in a visit a machine is opened, and how many of the tabs have been used.
    internal sealed class FirstSeenObserver : IViewObserver
    {
        private readonly HashSet<string> tabs = new HashSet<string>();

        public async Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink)
        {
            if (now.Machine != null && sink.Once("machine:" + Analytics.MachineName(now.Machine))) await sink.Send("machine", ("machine", Analytics.MachineName(now.Machine)));
            if (now.Tab != null && tabs.Add(now.Tab)) await sink.Summary("tabs used", tabs.Count.ToString());
        }
    }

    // A page counts as read after 30 seconds open: a tutorial is a milestone, any other page a read.
    internal sealed class PageReadObserver : IViewObserver
    {
        private (string Kind, string Target)? page;
        private DateTime since;

        public async Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink)
        {
            if (now.Page != page)
            {
                page = now.Page;
                since = time;
            }
            if (page is { } open && (time - since).TotalSeconds >= 30) await Read(open, sink);
        }

        private static async Task Read((string Kind, string Target) open, IAnalyticsSink sink)
        {
            var guide = open.Kind == "guide" ? Guides.Find(open.Target) : null;
            // Each tutorial once, so the counts show how far through the series people get.
            if (guide?.Section == "Tutorials") await sink.Milestone("tutorial", "tutorial:" + guide.Id, ("tutorial", guide.Id));
            else if (guide != null || (open.Kind == "reference" && DeviceReference.Exists(open.Target)))
            {
                if (sink.Once($"read:{open.Kind}/{open.Target}")) await sink.Send("read", ("page", $"{open.Kind}/{open.Target}"));
            }
        }
    }

    // Errors that stay for two minutes while the person is busy with the app, not while the tab sits idle, are where they
    // got stuck; when the errors are gone, they got unstuck.
    internal sealed class StuckObserver : IViewObserver
    {
        private string errors;
        private double errorSeconds;
        private DateTime lastObserved;
        private readonly HashSet<string> stuck = new HashSet<string>();

        public async Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink)
        {
            if (now.Errors != errors)
            {
                if (errors != null && now.Errors == null) await Unstuck(sink);
                errors = now.Errors;
                errorSeconds = 0;
            }
            else if (errors != null && now.Active && lastObserved != default)
            {
                errorSeconds += Math.Min(10, (time - lastObserved).TotalSeconds);
                if (errorSeconds >= 120 && stuck.Add(errors)) await sink.Send("stuck", ("where", errors), ("machine", Analytics.MachineName(now.Machine)));
            }
            lastObserved = time;
        }

        private async Task Unstuck(IAnalyticsSink sink)
        {
            foreach (var where in stuck.ToList()) await sink.Send("unstuck", ("where", where));
            stuck.Clear();
        }
    }
}
