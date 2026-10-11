using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Run
{
    public sealed record PanelActions(
        EventCallback<string> Toggle,
        EventCallback<string> StartDrag,
        EventCallback EndDrag,
        EventCallback<Place> DragOver,
        EventCallback<Place> Drop,
        EventCallback<Place> ShowTab,
        EventCallback ToggleSplit);
}
