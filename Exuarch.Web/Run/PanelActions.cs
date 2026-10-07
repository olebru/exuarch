using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Run
{
    // What the panels' heads, tabs and rails ask of the layout. The Run view carries each out, keeps the layout in the
    // browser and draws itself again.
    public sealed record PanelActions(
        // Folds a panel, a column or the panels below away, or opens it, by its key (see RunLayout).
        EventCallback<string> Toggle,
        EventCallback<string> StartDrag,
        EventCallback EndDrag,
        EventCallback<Place> DragOver,
        EventCallback<Place> Drop,
        // Shows a tab below.
        EventCallback<Place> ShowTab,
        EventCallback ToggleSplit);
}
