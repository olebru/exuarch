using System;

namespace Exuarch.Web.Run
{
    // How often a run draws the whole view. A pacer that rations it (max speed) gets the view drawn only every budget
    // milliseconds: drawing it takes the same time however long the frames ran, so the budget grows to keep it to about
    // a tenth of the time. The view updates less often, but the machine runs faster. Every other frame is drawn.
    public sealed class RedrawThrottle
    {
        // The shortest budget: a frame's time, which is also how long a click can wait.
        public const int ShortestMilliseconds = ClockPacing.FrameMilliseconds;
        // The longest the whole view goes undrawn.
        public const int LongestMilliseconds = 250;
        // The view is drawn after this many times as long as drawing it took, about a tenth of the time.
        public const double OverheadShare = 9;

        // From the last draw to the start of the next frame, which is what drawing costs.
        private readonly ElapsedClock between;
        private readonly ElapsedClock sinceView;
        private bool drawn;

        public RedrawThrottle(TimeProvider time)
        {
            between = new ElapsedClock(time);
            sinceView = new ElapsedClock(time);
        }

        public double BudgetMilliseconds { get; private set; } = ShortestMilliseconds;

        // A frame starts: what the last draw cost sets the budget.
        public void StartFrame()
        {
            if (drawn) BudgetMilliseconds = Math.Clamp(between.Milliseconds * OverheadShare, ShortestMilliseconds, LongestMilliseconds);
        }

        // Whether the frame ends with the view drawn: always when the run stops, and otherwise when the budget is up.
        public bool ShouldDraw(bool stopping, bool rationed)
        {
            drawn = stopping || !rationed || sinceView.Milliseconds >= BudgetMilliseconds;
            return drawn;
        }

        public void Draw(Action redraw)
        {
            between.Restart();
            redraw();
            sinceView.Restart();
        }
    }
}
