using System;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Workbench
{
    public sealed record WorkspaceTab(string Title, Func<TabBadge> Badge, Func<RenderFragment> Body);

    public readonly record struct TabBadge(int Count, string Kind)
    {
        public static readonly TabBadge None = new TabBadge(0, null);
    }
}
