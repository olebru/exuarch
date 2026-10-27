using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    public interface IRunHost
    {
        void Redraw();
        void ShowScreens();
        Task NextTurn();
        Task Delay(int milliseconds);
    }

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

        public async Task Run(Machine machine, ElapsedClock clock, CancellationToken stop)
        {
            var doubles = machine.Devices.OfType<DoubleFramebuffer>().ToArray();
            long SwapsOnAllScreens()
            {
                long swaps = 0;
                foreach (var screen in doubles) swaps += screen.Swaps;
                return swaps;
            }
            var frame = new RunFrame(new ElapsedClock(time), session.Tick, every => machine.RecordHistory = every, traced => machine.TraceTicks = traced, SwapsOnAllScreens);
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
                else if (frame.ScreenSwapped) host.ShowScreens();
                if (!keepGoing) break;
                await (pacer.WaitMilliseconds(frame.Clock.Milliseconds) is int wait ? host.Delay(wait) : host.NextTurn());
            }
        }
    }
}
