using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;

namespace Exuarch.Web
{
    internal interface IAnalyticsSink
    {
        bool Once(string key);
        Task Send(string name, params (string Key, string Value)[] data);
        Task Milestone(string name, string once, params (string Key, string Value)[] data);
        Task Summary(string key, string value);
    }

    internal interface IViewObserver
    {
        Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink);
    }

    internal sealed class FirstSeenObserver : IViewObserver
    {
        private readonly HashSet<string> tabs = new HashSet<string>();

        public async Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink)
        {
            if (now.Machine != null && sink.Once("machine:" + Analytics.MachineName(now.Machine))) await sink.Send("machine", ("machine", Analytics.MachineName(now.Machine)));
            if (now.Tab != null && tabs.Add(now.Tab)) await sink.Summary("tabs used", tabs.Count.ToString());
        }
    }

    internal sealed class PageReadObserver : IViewObserver
    {
        private const double SecondsOpenToCountAsRead = 30;
        private (string Kind, string Target)? page;
        private DateTime since;

        public async Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink)
        {
            if (now.Page != page)
            {
                page = now.Page;
                since = time;
            }
            if (page is { } open && (time - since).TotalSeconds >= SecondsOpenToCountAsRead) await Read(open, sink);
        }

        private static async Task Read((string Kind, string Target) open, IAnalyticsSink sink)
        {
            var guide = open.Kind == "guide" ? Guides.Find(open.Target) : null;
            if (guide?.Section == "Tutorials") await sink.Milestone("tutorial", "tutorial:" + guide.Id, ("tutorial", guide.Id));
            else if (guide != null || (open.Kind == "reference" && DeviceReference.Exists(open.Target)))
            {
                if (sink.Once($"read:{open.Kind}/{open.Target}")) await sink.Send("read", ("page", $"{open.Kind}/{open.Target}"));
            }
        }
    }

    internal sealed class StuckObserver : IViewObserver
    {
        private const double ActiveSecondsWithErrorsToCountAsStuck = 120;
        private const double MaxSecondsCountedBetweenObservations = 10;
        private string errors;
        private double activeSecondsWithErrors;
        private DateTime lastObserved;
        private readonly HashSet<string> stuck = new HashSet<string>();

        public async Task Observe(Analytics.View now, DateTime time, IAnalyticsSink sink)
        {
            if (now.Errors != errors)
            {
                if (errors != null && now.Errors == null) await Unstuck(sink);
                errors = now.Errors;
                activeSecondsWithErrors = 0;
            }
            else if (errors != null && now.Active && lastObserved != default)
            {
                activeSecondsWithErrors += Math.Min(MaxSecondsCountedBetweenObservations, (time - lastObserved).TotalSeconds);
                if (activeSecondsWithErrors >= ActiveSecondsWithErrorsToCountAsStuck && stuck.Add(errors)) await sink.Send("stuck", ("where", errors), ("machine", Analytics.MachineName(now.Machine)));
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
