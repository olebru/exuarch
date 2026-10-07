using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // What a run needs from the page it runs in.
    public interface IRunHost
    {
        // Draws the whole view again.
        void Redraw();
        // Sends only what the screens drew since they were last shown, without drawing the rest of the view.
        void ShowScreens();
        // Gives the browser its turn: clicks, keys and drawing.
        Task NextTurn();
        Task Delay(int milliseconds);
    }

    // Runs a machine frame after frame until it is stopped, halts, hits a breakpoint or fails, or the view moves on to
    // another machine. Each frame its pacer, chosen afresh from the speed settings, runs the ticks; then the view is
    // drawn, or at max speed only the screens when they swapped in a finished frame, and the browser has its turn.
    public sealed class RunLoop
    {
        private readonly RunSession session;
        private readonly IRunHost host;
        private readonly TimeProvider time;

        public RunLoop(RunSession session, IRunHost host, TimeProvider time)
        {
            this.session = session;
            this.host = host;
            this.time = time;
        }

        // clock times the whole run; the run's speed samples go to the session's meter.
        public async Task Run(Machine machine, ElapsedClock clock, CancellationToken stop)
        {
            // A double buffered screen shows a finished frame when it swaps. At max speed the run stops there to show it,
            // sending only the screen's changes; otherwise the screen would only show the frames that happened to be in
            // front when the view was drawn.
            var doubles = machine.Devices.OfType<DoubleFramebuffer>().ToArray();
            var frame = new RunFrame(new ElapsedClock(time), session.Tick, every => machine.RecordHistory = every, () => doubles.Sum(screen => screen.Swaps));
            var throttle = new RedrawThrottle(time);
            double lastFrameAt = 0;
            session.Meter.StartRun(machine.Cycles);
            while (!stop.IsCancellationRequested && ReferenceEquals(machine, session.Machine))
            {
                throttle.StartFrame();
                frame.Clock.Restart();
                double now = clock.Seconds;
                frame.Start(now - lastFrameAt);
                var pacer = session.Speed.Pacer();
                bool keepGoing = pacer.RunFrame(frame);
                lastFrameAt = now;
                session.Meter.Sample(now, machine.Cycles);
                if (throttle.ShouldDraw(!keepGoing, pacer.RationsRedraws)) throttle.Draw(host.Redraw);
                else if (frame.Swapped) host.ShowScreens();
                if (!keepGoing) break;
                await (pacer.WaitMilliseconds(frame.Clock.Milliseconds) is int wait ? host.Delay(wait) : host.NextTurn());
            }
        }
    }
}
