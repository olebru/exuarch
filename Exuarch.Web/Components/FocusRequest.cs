namespace Exuarch.Web.Components
{
    // Another view asks an editor to show something: a device, or an instruction and its step. Each request has a new
    // version, so asking for the same thing twice still shows it again.
    public sealed record FocusRequest(int Version, string Target, int Step = 0)
    {
        public static readonly FocusRequest None = new FocusRequest(0, null);
    }
}
