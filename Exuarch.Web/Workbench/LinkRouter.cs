using System;
using System.Collections.Generic;

namespace Exuarch.Web.Workbench
{
    // Where an exuarch: link goes, by its kind (device, instruction, program, tab, package, guide, reference). A kind
    // with no route is ignored.
    public sealed class LinkRouter
    {
        private readonly Dictionary<string, Action<(string Kind, string Target)>> routes = new Dictionary<string, Action<(string Kind, string Target)>>();

        public LinkRouter On(string kind, Action<(string Kind, string Target)> follow)
        {
            routes[kind] = follow;
            return this;
        }

        public bool Follow((string Kind, string Target) link)
        {
            if (!routes.TryGetValue(link.Kind, out var follow)) return false;
            follow(link);
            return true;
        }
    }
}
