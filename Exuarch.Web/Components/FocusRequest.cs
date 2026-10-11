namespace Exuarch.Web.Components
{
    public sealed record FocusRequest(int Version, string Target, int Step = 0)
    {
        public static readonly FocusRequest None = new FocusRequest(0, null);
    }
}
