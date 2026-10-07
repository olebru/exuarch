namespace Exuarch.Web.Workbench
{
    // A built in machine opened with one of its programs, run at a speed for the Run view: "slow" or "max".
    public sealed record Showcase(string Package, string Program, string Speed);
}
