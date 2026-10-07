using System;
using Exuarch.Core;

namespace Exuarch.Web.Workbench
{
    public enum SpeedKind { Any, Min, Exact }

    // The clock a program needs: of any speed, of at least Hz, or of exactly Hz. A kind other than Any may be picked
    // before its speed is typed; it says nothing about the clock until then.
    public readonly record struct SpeedNeed(SpeedKind Kind, int? Hz)
    {
        // What the program's needs say, or the kind picked while they say nothing about the clock.
        public static SpeedNeed Of(ProgramNeeds needs, SpeedKind picked)
        {
            if (needs?.ExactHz != null) return new SpeedNeed(SpeedKind.Exact, needs.ExactHz);
            if (needs?.MinHz != null) return new SpeedNeed(SpeedKind.Min, needs.MinHz);
            return new SpeedNeed(picked, null);
        }

        public void ApplyTo(ProgramNeeds needs)
        {
            needs.MinHz = Kind == SpeedKind.Min ? Hz : null;
            needs.ExactHz = Kind == SpeedKind.Exact ? Hz : null;
        }

        // The kind as the page's choice writes it: "any", "min" or "exact".
        public string Value => Kind.ToString().ToLowerInvariant();

        public static SpeedKind Parse(string value) => Enum.Parse<SpeedKind>(value, ignoreCase: true);
    }
}
