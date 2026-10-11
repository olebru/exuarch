using Exuarch.Core;

namespace Exuarch.Web.Run
{
    public sealed class RunSpeed
    {
        public static RunSpeed Session { get; } = new RunSpeed();

        public const int SliderSteps = ClockSlider.Steps;
        public const int SixteenHzStep = 201;

        public int Slider { get; set; } = SixteenHzStep;
        public bool Max { get; set; }

        public int Hz => ClockSlider.HzAt(Slider);

        public IClockPacer Pacer() => Max ? new MaxSpeedPacer() : new FixedHzPacer(Hz);
    }
}
