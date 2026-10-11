using System;
using Exuarch.Core;

namespace Exuarch.Web.Workbench
{
    public enum SpeedKind { Any, Min, Exact }

    public readonly record struct SpeedNeed(SpeedKind Kind, int? Hz)
    {
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

        public string Value => Kind.ToString().ToLowerInvariant();

        public static SpeedKind Parse(string value) => Enum.Parse<SpeedKind>(value, ignoreCase: true);
    }
}
