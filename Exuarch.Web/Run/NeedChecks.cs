using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    public static class NeedChecks
    {
        public static bool KeypadMissing(ProgramNeeds needs, RunSession session)
        {
            return needs?.Keypad == true && session.Keys.Keypad == null && session.Machine.Devices.OfType<Keypad>().Any();
        }

        public static bool SpeedTooLow(ProgramNeeds needs, RunSpeed speed)
        {
            return needs?.MinHz is int minHz && !speed.Max && speed.Hz < minHz;
        }

        public static bool SpeedNotExact(ProgramNeeds needs, RunSpeed speed)
        {
            return needs?.ExactHz is int exactHz && (speed.Max || speed.Hz != exactHz);
        }

        public static void MeetMinHz(int minHz, RunSpeed speed)
        {
            if (ClockSlider.StepAtLeast(minHz) is int step) speed.Slider = step;
            else speed.Max = true;
        }

        public static void MeetExactHz(int exactHz, RunSpeed speed)
        {
            speed.Slider = ClockSlider.StepAtLeast(ClockSlider.Nearest(exactHz)) ?? speed.Slider;
            speed.Max = false;
        }
    }
}
