using Hider.Core.Scheduling;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Scheduling
{
    /// <summary>Each rule of RefreshScheduler on its own (FR-50, FR-51, D-10).</summary>
    public class RefreshSchedulerCasesTests
    {
        private static readonly int[] ticksAfterStart = { 1100, 1200 };
        private static readonly int[] ticksSameInterval = { 100, 200 };
        private static readonly int[] ticksLonger = { 100, 1100 };
        private static readonly int[] ticksAfterPause = { 1100 };
        private static readonly RefreshReason[] bothHandlers = { RefreshReason.Manual, RefreshReason.Manual };

        private static (RefreshScheduler, FakeTimeProvider, List<(int Ms, RefreshReason Reason)>) Create(bool _endRunAtOnce)
        {
            var time = new FakeTimeProvider();
            var scheduler = new RefreshScheduler(time);
            var log = new List<(int, RefreshReason)>();
            scheduler.Due += _r =>
            {
                log.Add(((int)time.Elapsed.TotalMilliseconds, _r));
                if (_endRunAtOnce)
                {
                    scheduler.EndRun();
                }
            };
            return (scheduler, time, log);
        }

        [Fact]
        public void RefreshReason_Values()
        {
            Assert.Equal(new[] { RefreshReason.Created, RefreshReason.Committed, RefreshReason.Manual, RefreshReason.Timer }, Enum.GetValues<RefreshReason>());
        }

        [Fact]
        public void IntervalMs_StartsAtZero_FollowsConfigure()
        {
            var (scheduler, time, _) = Create(true);
            Assert.Equal(0, scheduler.IntervalMs);
            Assert.Equal(0, time.ActiveTimers);
            scheduler.Configure(250);
            Assert.Equal(250, scheduler.IntervalMs);
            Assert.Equal(1, time.ActiveTimers);
            scheduler.Configure(100);
            Assert.Equal(100, scheduler.IntervalMs);
            Assert.Equal(1, time.ActiveTimers); // the same timer, re-armed
            scheduler.Configure(0);
            Assert.Equal(0, scheduler.IntervalMs);
            Assert.Equal(0, time.ActiveTimers);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void Configure_Negative_IsZero_StopsTheTimer(int _interval)
        {
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(100);
            scheduler.Configure(_interval);
            Assert.Equal(0, scheduler.IntervalMs);
            Assert.Equal(0, time.ActiveTimers);
            time.Advance(TimeSpan.FromSeconds(10));
            Assert.Empty(log);
        }

        [Fact]
        public void Configure_NegativeWhileStopped_StaysStopped()
        {
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(-5);
            Assert.Equal(0, scheduler.IntervalMs);
            Assert.Equal(0, time.ActiveTimers);
            time.Advance(TimeSpan.FromSeconds(10));
            Assert.Empty(log);
        }

        [Fact]
        public void Configure_SameInterval_DoesNotRestartTheTimer()
        {
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(100);
            time.Advance(TimeSpan.FromMilliseconds(50));
            scheduler.Configure(100);
            time.Advance(TimeSpan.FromMilliseconds(150));
            Assert.Equal(ticksSameInterval, log.Select(_e => _e.Ms));
        }

        [Fact]
        public void Configure_FromZero_StartsOneIntervalFromNow()
        {
            var (scheduler, time, log) = Create(true);
            time.Advance(TimeSpan.FromMilliseconds(1000));
            scheduler.Configure(100);
            Assert.Empty(log); // the change itself captures nothing (D-10)
            time.Advance(TimeSpan.FromMilliseconds(200));
            Assert.Equal(ticksAfterStart, log.Select(_e => _e.Ms));
        }

        [Fact]
        public void Configure_Longer_KeepsTheScheduledTick_ThenTheNewInterval()
        {
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(100);
            time.Advance(TimeSpan.FromMilliseconds(50));
            scheduler.Configure(1000);
            time.Advance(TimeSpan.FromMilliseconds(1050));
            Assert.Equal(ticksLonger, log.Select(_e => _e.Ms));
        }

        [Fact]
        public void Configure_ShorterButNotSooner_KeepsTheScheduledTick()
        {
            // 100 ms left of 200; the new interval (100) is not sooner, so the tick stays at 200.
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(200);
            time.Advance(TimeSpan.FromMilliseconds(100));
            scheduler.Configure(100);
            time.Advance(TimeSpan.FromMilliseconds(99));
            Assert.Empty(log);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(200, Assert.Single(log).Ms);
            time.Advance(TimeSpan.FromMilliseconds(100));
            Assert.Equal(300, log[^1].Ms);
        }

        [Fact]
        public void Configure_AfterDispose_IsIgnored()
        {
            var (scheduler, time, log) = Create(true);
            scheduler.Dispose();
            scheduler.Configure(100);
            Assert.Equal(0, scheduler.IntervalMs);
            Assert.Equal(0, time.ActiveTimers);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(log);
        }

        [Fact]
        public void Dispose_StopsTheTimer_AndIsIdempotent()
        {
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(100);
            scheduler.Dispose();
            Assert.Equal(0, time.ActiveTimers);
            scheduler.Dispose();
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(log);
            Assert.Equal(100, scheduler.IntervalMs); // the last value stays readable
        }

        [Fact]
        public void Dispose_DropsTheMergedRequest()
        {
            var (scheduler, _, log) = Create(false);
            scheduler.Request(RefreshReason.Created);
            scheduler.Request(RefreshReason.Manual); // merged behind the running capture
            scheduler.Dispose();
            scheduler.EndRun();
            Assert.Equal(RefreshReason.Created, Assert.Single(log).Reason);
        }

        [Fact]
        public void TimerPaused_GetAndSet()
        {
            var (scheduler, _, _) = Create(true);
            Assert.False(scheduler.TimerPaused);
            scheduler.TimerPaused = true;
            Assert.True(scheduler.TimerPaused);
            scheduler.TimerPaused = false;
            Assert.False(scheduler.TimerPaused);
        }

        [Fact]
        public void TimerPaused_TicksAreSkippedButTheTimerKeepsItsRhythm()
        {
            var (scheduler, time, log) = Create(true);
            scheduler.Configure(100);
            scheduler.TimerPaused = true;
            time.Advance(TimeSpan.FromMilliseconds(1050));
            Assert.Empty(log);
            Assert.Equal(1, time.ActiveTimers);
            scheduler.TimerPaused = false;
            time.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Equal(ticksAfterPause, log.Select(_e => _e.Ms));
        }

        [Fact]
        public void Request_WithoutAHandler_StartsNoRun()
        {
            // Nobody would call EndRun: the next request after a handler is attached must not be stuck behind it.
            var time = new FakeTimeProvider();
            using var scheduler = new RefreshScheduler(time);
            scheduler.Request(RefreshReason.Created);
            var log = new List<RefreshReason>();
            scheduler.Due += log.Add;
            scheduler.Request(RefreshReason.Manual);
            Assert.Equal(RefreshReason.Manual, Assert.Single(log));
        }

        [Fact]
        public void EndRun_WithoutARun_RaisesNothing()
        {
            var (scheduler, _, log) = Create(false);
            scheduler.EndRun();
            scheduler.EndRun();
            Assert.Empty(log);
            scheduler.Request(RefreshReason.Committed);
            Assert.Equal(RefreshReason.Committed, Assert.Single(log).Reason);
        }

        [Fact]
        public void Merge_ARequestReplacesATimerTick()
        {
            var (scheduler, time, log) = Create(false);
            scheduler.Configure(100);
            scheduler.Request(RefreshReason.Created);
            time.Advance(TimeSpan.FromMilliseconds(100)); // tick merged
            scheduler.Request(RefreshReason.Committed); // replaces the tick
            scheduler.EndRun();
            Assert.Equal(RefreshReason.Committed, log[^1].Reason);
            Assert.Equal(2, log.Count);
        }

        [Fact]
        public void Merge_ATimerTickNeverReplacesARequest()
        {
            var (scheduler, time, log) = Create(false);
            scheduler.Configure(100);
            scheduler.Request(RefreshReason.Created);
            scheduler.Request(RefreshReason.Manual);
            time.Advance(TimeSpan.FromMilliseconds(300)); // three ticks while the run is outstanding
            scheduler.EndRun();
            Assert.Equal(RefreshReason.Manual, log[^1].Reason);
            Assert.Equal(2, log.Count);
        }

        [Fact]
        public void Merge_TheFirstRequestIsKept()
        {
            var (scheduler, _, log) = Create(false);
            scheduler.Request(RefreshReason.Created);
            scheduler.Request(RefreshReason.Manual);
            scheduler.Request(RefreshReason.Committed);
            scheduler.EndRun();
            Assert.Equal(new[] { RefreshReason.Created, RefreshReason.Manual }, log.Select(_e => _e.Reason));
            scheduler.EndRun();
            Assert.Equal(2, log.Count); // merged into one: nothing left
        }

        [Fact]
        public void Merge_RequestFromInsideTheHandler_RaisedAfterEndRun()
        {
            var time = new FakeTimeProvider();
            using var scheduler = new RefreshScheduler(time);
            var log = new List<RefreshReason>();
            scheduler.Due += _r =>
            {
                log.Add(_r);
                if (_r == RefreshReason.Created)
                {
                    scheduler.Request(RefreshReason.Manual); // the run has started: merged, not nested
                    Assert.Single(log);
                }
            };
            scheduler.Request(RefreshReason.Created);
            Assert.Single(log);
            scheduler.EndRun();
            Assert.Equal(new[] { RefreshReason.Created, RefreshReason.Manual }, log);
        }

        [Fact]
        public void EndRun_HandlerRemovedMeanwhile_MergedRequestIsDropped()
        {
            var time = new FakeTimeProvider();
            using var scheduler = new RefreshScheduler(time);
            var log = new List<RefreshReason>();
            Action<RefreshReason> handler = log.Add;
            scheduler.Due += handler;
            scheduler.Request(RefreshReason.Created);
            scheduler.Request(RefreshReason.Manual);
            scheduler.Due -= handler;
            scheduler.EndRun(); // raised to nobody: no run starts
            Assert.Single(log);
            scheduler.Due += handler;
            scheduler.Request(RefreshReason.Committed);
            Assert.Equal(RefreshReason.Committed, log[^1]);
        }

        [Fact]
        public void Due_EveryHandlerGetsTheReason_OneRun()
        {
            var time = new FakeTimeProvider();
            using var scheduler = new RefreshScheduler(time);
            var log = new List<RefreshReason>();
            scheduler.Due += log.Add;
            scheduler.Due += log.Add;
            scheduler.Request(RefreshReason.Manual);
            Assert.Equal(bothHandlers, log);
            scheduler.Request(RefreshReason.Created); // merged: one run for both handlers
            Assert.Equal(2, log.Count);
        }

        [Fact]
        public void Tick_AfterDispose_IsIgnored()
        {
            // A real timer's callback can already be on its way when Dispose runs.
            var time = new KeepingTimeProvider();
            var scheduler = new RefreshScheduler(time);
            var log = new List<RefreshReason>();
            scheduler.Due += log.Add;
            scheduler.Configure(100);
            var timer = Assert.Single(time.Timers);
            scheduler.Dispose();
            Assert.True(timer.Disposed);
            timer.Fire();
            Assert.Empty(log);
        }

        [Fact]
        public void Tick_OfAStoppedTimer_IsIgnored()
        {
            var time = new KeepingTimeProvider();
            using var scheduler = new RefreshScheduler(time);
            var log = new List<RefreshReason>();
            scheduler.Due += _r =>
            {
                log.Add(_r);
                scheduler.EndRun();
            };
            scheduler.Configure(100);
            var timer = Assert.Single(time.Timers);
            timer.Fire();
            Assert.Equal(RefreshReason.Timer, Assert.Single(log));
            Assert.Equal(TimeSpan.FromMilliseconds(100), timer.DueTime); // re-armed for the next tick
            scheduler.Configure(0);
            Assert.True(timer.Disposed);
            timer.Fire(); // late callback of the stopped timer
            Assert.Single(log);
        }

        [Fact]
        public void Arm_UsesTheTimestampFrequency()
        {
            // 1000 ms from t = 5 s on a clock counting microseconds: the tick is due at 6 000 000.
            var time = new KeepingTimeProvider { Now = 5_000_000 };
            using var scheduler = new RefreshScheduler(time);
            scheduler.Configure(1000);
            var timer = Assert.Single(time.Timers);
            Assert.Equal(TimeSpan.FromMilliseconds(1000), timer.DueTime);
            Assert.Equal(Timeout.InfiniteTimeSpan, timer.Period);
            time.Now += 950_000; // 50 ms left
            scheduler.Configure(100);
            Assert.Equal(TimeSpan.FromMilliseconds(1000), timer.DueTime); // 100 ms is not sooner than the 50 left
            time.Now = 5_000_000 + 100_000; // back: 900 ms left
            scheduler.Configure(10);
            Assert.Equal(TimeSpan.FromMilliseconds(10), timer.DueTime);
        }

        /// <summary>A clock whose timers keep their callback after Dispose, so a late tick can be delivered by hand.</summary>
        private sealed class KeepingTimeProvider : TimeProvider
        {
            public List<KeptTimer> Timers { get; } = new();

            public long Now { get; set; }

            public override long TimestampFrequency => 1_000_000;

            public override long GetTimestamp()
            {
                return Now;
            }

            public override ITimer CreateTimer(TimerCallback _callback, object? _state, TimeSpan _dueTime, TimeSpan _period)
            {
                var timer = new KeptTimer(_callback, _state) { DueTime = _dueTime, Period = _period };
                Timers.Add(timer);
                return timer;
            }
        }

        private sealed class KeptTimer(TimerCallback _callback, object? _state) : ITimer
        {
            public TimeSpan DueTime { get; set; }
            public TimeSpan Period { get; set; }
            public bool Disposed { get; private set; }

            public void Fire()
            {
                _callback(_state);
            }

            public bool Change(TimeSpan _dueTime, TimeSpan _period)
            {
                DueTime = _dueTime;
                Period = _period;
                return true;
            }

            public void Dispose()
            {
                Disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
