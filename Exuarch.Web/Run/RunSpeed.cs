using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // The speed a run goes at. There is one for the session, so it survives switching tabs and machines.
    public sealed class RunSpeed
    {
        public static RunSpeed Session { get; } = new RunSpeed();

        public const int SliderSteps = ClockSlider.Steps;

        // The speed slider's position, 0 to SliderSteps; it starts at 16 Hz.
        public int Slider { get; set; } = 201;
        // Ignore the slider and run as many ticks as the browser allows.
        public bool Max { get; set; }

        public int Hz => ClockSlider.HzAt(Slider);

        // How the next frame of a run keeps time, decided afresh every frame so a change takes effect at once.
        public IClockPacer Pacer() => Max ? new MaxSpeedPacer() : new FixedHzPacer(Hz);
    }
}
