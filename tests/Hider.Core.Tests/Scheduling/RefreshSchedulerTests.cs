using Hider.Core.Scheduling;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Scheduling
{
    public class RefreshSchedulerTests
    {
        private static readonly int[] tickTimesMs = { 100, 400, 700 };
        private static readonly int[] ticksAfterShortening = { 1100, 1200 };

        /// <param name="_endRunAtOnce">The handler ends each run right away, as a capture that finishes instantly.</param>
        private static (RefreshScheduler, FakeTimeProvider, List<(TimeSpan, RefreshReason)>) Create(bool _endRunAtOnce = false)
        {
            var time = new FakeTimeProvider();
            var scheduler = new RefreshScheduler(time);
            var log = new List<(TimeSpan, RefreshReason)>();
            scheduler.Due += _r =>
            {
                log.Add((time.Elapsed, _r));
                if (_endRunAtOnce)
                {
                    scheduler.EndRun();
                }
            };
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
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(100);
            time.Advance(TimeSpan.FromMilliseconds(1000));
            Assert.Equal(10, log.Count);
            Assert.All(log, _e => Assert.Equal(RefreshReason.Timer, _e.Item2));
            Assert.Equal(Enumerable.Range(1, 10).Select(_i => TimeSpan.FromMilliseconds(_i * 100)), log.Select(_e => _e.Item1));
        }

        [Fact]
        public void CT39_Requests_RaiseImmediately()
        {
            var (scheduler, _, log) = Create(true);
            scheduler.Request(RefreshReason.Created);
            scheduler.Request(RefreshReason.Committed);
            scheduler.Request(RefreshReason.Manual);
            Assert.Equal(new[] { RefreshReason.Created, RefreshReason.Committed, RefreshReason.Manual }, log.Select(_e => _e.Item2));
            Assert.All(log, _e => Assert.Equal(TimeSpan.Zero, _e.Item1));
        }

        [Fact]
        public void ShorterInterval_TicksOneNewIntervalFromTheChange()
        {
            // An hour changed to 100 ms must not wait for the hour; nothing is captured by the change itself (D-10).
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(3_600_000);
            time.Advance(TimeSpan.FromMilliseconds(1000));
            scheduler.Configure(100);
            Assert.Empty(log);
            time.Advance(TimeSpan.FromMilliseconds(250));
            Assert.Equal(ticksAfterShortening, log.Select(_e => (int)_e.Item1.TotalMilliseconds));
        }

        [Fact]
        public void ShorterInterval_NeverPutsTheScheduledTickLater()
        {
            // 59 s into 60 s, changing to 30 s keeps the tick 1 s away instead of moving it to 30 s from now.
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(60_000);
            time.Advance(TimeSpan.FromSeconds(59));
            scheduler.Configure(30_000);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(60_000, (int)Assert.Single(log).Item1.TotalMilliseconds);
            time.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal(90_000, (int)log[^1].Item1.TotalMilliseconds);
        }

        [Fact]
        public void CT40_MergingConfigureDisposeAndDrag()
        {
            var (scheduler, time, log) = Create();

            // Raising Due starts a run; requests until EndRun merge into one, raised after it; a request beats a timer tick.
            scheduler.Configure(100);
            scheduler.Request(RefreshReason.Created);
            log.Clear();
            scheduler.Request(RefreshReason.Manual);
            time.Advance(TimeSpan.FromMilliseconds(250));
            scheduler.Request(RefreshReason.Committed);
            Assert.Empty(log);
            scheduler.EndRun();
            Assert.Equal(new[] { RefreshReason.Manual }, log.Select(_e => _e.Item2));
            scheduler.EndRun();
            Assert.Single(log);

            // A timer-only backlog becomes one Timer request (the tick at 300 ms starts a run, the next two merge).
            log.Clear();
            time.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Single(log);
            scheduler.EndRun();
            Assert.Equal(new[] { RefreshReason.Timer, RefreshReason.Timer }, log.Select(_e => _e.Item2));
            scheduler.EndRun();

            // Configure takes effect from the next period: the tick already scheduled stays, then the new interval.
            var (s2, t2, log2) = Create(true);
            s2.Configure(100);
            t2.Advance(TimeSpan.FromMilliseconds(50));
            s2.Configure(300);
            t2.Advance(TimeSpan.FromMilliseconds(700));
            Assert.Equal(tickTimesMs, log2.Select(_e => (int)_e.Item1.TotalMilliseconds));
            s2.Dispose();

            // Paused timer (drag with a non-live capture) skips ticks but keeps requests.
            log.Clear();
            scheduler.TimerPaused = true;
            time.Advance(TimeSpan.FromMilliseconds(900));
            scheduler.Request(RefreshReason.Committed);
            Assert.Equal(new[] { RefreshReason.Committed }, log.Select(_e => _e.Item2));
            scheduler.EndRun();
            scheduler.TimerPaused = false;
            log.Clear();
            for (int i = 0; i < 3; i++)
            {
                time.Advance(TimeSpan.FromMilliseconds(100));
                scheduler.EndRun();
            }
            Assert.Equal(3, log.Count(_e => _e.Item2 == RefreshReason.Timer));

            // Ticks while a run is outstanding (Due raised, the posted capture not started yet) do not raise Due again.
            log.Clear();
            time.Advance(TimeSpan.FromMilliseconds(100));
            time.Advance(TimeSpan.FromMilliseconds(100));
            time.Advance(TimeSpan.FromMilliseconds(100));
            Assert.Single(log);
            scheduler.EndRun();
            Assert.Equal(2, log.Count);
            scheduler.EndRun();

            // Nothing fires after Dispose.
            log.Clear();
            scheduler.Dispose();
            time.Advance(TimeSpan.FromHours(1));
            scheduler.Request(RefreshReason.Manual);
            Assert.Empty(log);
        }
    }
}
