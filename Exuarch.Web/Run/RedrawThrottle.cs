using System;

namespace Exuarch.Web.Run
{
    public sealed class RedrawThrottle
    {
        public const int ShortestMilliseconds = ClockPacing.FrameMilliseconds;
        public const int LongestMilliseconds = 250;
        public const double BudgetTimesDrawCost = 9;

        private readonly ElapsedClock sinceDrawStarted;
        private readonly ElapsedClock sinceDrawn;
        private bool drawn;

        public RedrawThrottle(TimeProvider time)
        {
            sinceDrawStarted = new ElapsedClock(time);
            sinceDrawn = new ElapsedClock(time);
        }

        public double BudgetMilliseconds { get; private set; } = ShortestMilliseconds;

        public void StartFrame()
        {
            if (drawn) BudgetMilliseconds = Math.Clamp(sinceDrawStarted.Milliseconds * BudgetTimesDrawCost, ShortestMilliseconds, LongestMilliseconds);
        }

        public bool ShouldDraw(bool stopping, bool rationed)
        {
            drawn = stopping || !rationed || sinceDrawn.Milliseconds >= BudgetMilliseconds;
            return drawn;
        }

        public void Draw(Action redraw)
        {
            sinceDrawStarted.Restart();
            redraw();
            sinceDrawn.Restart();
        }
    }
}
