using Exuarch.Core;

namespace Exuarch.Web.Hardware
{
    // A device type in the palette the pointer is over, and where its card goes: fixed, beside the palette.
    public sealed record TypeTip(DeviceTypeInfo Info, double Left, double Top);
}
