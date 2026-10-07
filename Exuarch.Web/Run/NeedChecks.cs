using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // What a program needs to work that the Run view does not give it now, and how to give it.
    public static class NeedChecks
    {
        // It reads the keypad, and no keypad has the keyboard.
        public static bool KeypadMissing(ProgramNeeds needs, RunSession session)
        {
            return needs?.Keypad == true && session.Keys.Keypad == null && session.Machine.Devices.OfType<Keypad>().Any();
        }

        public static bool SpeedTooLow(ProgramNeeds needs, RunSpeed speed)
        {
            return needs?.MinHz is int minHz && !speed.Max && speed.Hz < minHz;
        }

        // Max is no speed in particular, so it does not meet an exact one.
        public static bool SpeedNotExact(ProgramNeeds needs, RunSpeed speed)
        {
            return needs?.ExactHz is int exactHz && (speed.Max || speed.Hz != exactHz);
        }

        // The slider at the speed, or max speed when the slider does not go that fast.
        public static void MeetMinHz(int minHz, RunSpeed speed)
        {
            if (ClockSlider.StepAtLeast(minHz) is int step) speed.Slider = step;
            else speed.Max = true;
        }

        // The slider as near the speed as it goes.
        public static void MeetExactHz(int exactHz, RunSpeed speed)
        {
            speed.Slider = ClockSlider.StepAtLeast(ClockSlider.Nearest(exactHz)) ?? speed.Slider;
            speed.Max = false;
        }
    }
}
