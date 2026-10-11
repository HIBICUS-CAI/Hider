using Hider.Core.Scheduling;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Scheduling
{
    public class RefreshSchedulerTests
    {
        private static (RefreshScheduler, FakeTimeProvider, List<(TimeSpan, RefreshReason)>) Create()
        {
            var time = new FakeTimeProvider();
            var scheduler = new RefreshScheduler(time);
            var log = new List<(TimeSpan, RefreshReason)>();
            scheduler.Due += r => log.Add((time.Elapsed, r));
            return (scheduler, time, log);
        }

        [Fact]
        public void CT37_IntervalZero_NeverTicks()
        {
            var (scheduler, time, log) = Create();
            scheduler.Configure(0);
            time.Advance(TimeSpan.FromHours(1));
            Assert.Empty(log);
            Assert.Equal(0, time.ActiveTimers);
        }

        [Fact]
        public void CT38_Interval100_TicksEvery100ms()
        {
            var (scheduler, time, log) = Create();
            scheduler.Configure(100);
            time.Advance(TimeSpan.FromMilliseconds(1000));
            Assert.Equal(10, log.Count);
            Assert.All(log, e => Assert.Equal(RefreshReason.Timer, e.Item2));
            Assert.Equal(Enumerable.Range(1, 10).Select(i => TimeSpan.FromMilliseconds(i * 100)), log.Select(e => e.Item1));
        }

        [Fact]
        public void CT39_Requests_RaiseImmediately()
        {
            var (scheduler, _, log) = Create();
            scheduler.Request(RefreshReason.Created);
            scheduler.Request(RefreshReason.Committed);
            scheduler.Request(RefreshReason.Manual);
            Assert.Equal(new[] { RefreshReason.Created, RefreshReason.Committed, RefreshReason.Manual }, log.Select(e => e.Item2));
            Assert.All(log, e => Assert.Equal(TimeSpan.Zero, e.Item1));
        }

        [Fact]
        public void CT40_MergingConfigureDisposeAndDrag()
        {
            var (scheduler, time, log) = Create();

            // Requests while running merge into one, raised after EndRun; a request beats a timer tick.
            scheduler.Configure(100);
            scheduler.BeginRun();
            scheduler.Request(RefreshReason.Manual);
            time.Advance(TimeSpan.FromMilliseconds(250));
            scheduler.Request(RefreshReason.Committed);
            Assert.Empty(log);
            scheduler.EndRun();
            Assert.Equal(new[] { RefreshReason.Manual }, log.Select(e => e.Item2));
            scheduler.EndRun();
            Assert.Single(log);

            // A timer-only backlog becomes one Timer request.
            log.Clear();
            scheduler.BeginRun();
            time.Advance(TimeSpan.FromMilliseconds(300));
            scheduler.EndRun();
            Assert.Equal(new[] { RefreshReason.Timer }, log.Select(e => e.Item2));

            // Configure takes effect from the next period: the tick already scheduled stays, then the new interval.
            var (s2, t2, log2) = Create();
            s2.Configure(100);
            t2.Advance(TimeSpan.FromMilliseconds(50));
            s2.Configure(300);
            t2.Advance(TimeSpan.FromMilliseconds(700));
            Assert.Equal(new[] { 100, 400, 700 }, log2.Select(e => (int)e.Item1.TotalMilliseconds));
            s2.Dispose();

            // Paused timer (drag with a non-live capture) skips ticks but keeps requests.
            log.Clear();
            scheduler.TimerPaused = true;
            time.Advance(TimeSpan.FromMilliseconds(900));
            scheduler.Request(RefreshReason.Committed);
            Assert.Equal(new[] { RefreshReason.Committed }, log.Select(e => e.Item2));
            scheduler.TimerPaused = false;
            log.Clear();
            time.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Equal(3, log.Count(e => e.Item2 == RefreshReason.Timer));

            // Nothing fires after Dispose.
            log.Clear();
            scheduler.Dispose();
            time.Advance(TimeSpan.FromHours(1));
            scheduler.Request(RefreshReason.Manual);
            Assert.Empty(log);
        }
    }
}
