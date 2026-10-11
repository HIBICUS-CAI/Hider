namespace Hider.Core.Tests.Fakes
{
    /// <summary>Manually advanced clock for scheduler tests. Timers fire synchronously inside <see cref="Advance"/>.</summary>
    public sealed class FakeTimeProvider : TimeProvider
    {
        private readonly List<FakeTimer> timers = new();
        private long nowTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return nowTicks;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero).AddTicks(nowTicks);
        }

        public TimeSpan Elapsed => TimeSpan.FromTicks(nowTicks);

        public int ActiveTimers => timers.Count(_t => _t.DueAt is not null);

        public override ITimer CreateTimer(TimerCallback _callback, object? _state, TimeSpan _dueTime, TimeSpan _period)
        {
            var timer = new FakeTimer(this, _callback, _state);
            timers.Add(timer);
            timer.Change(_dueTime, _period);
            return timer;
        }

        /// <summary>Moves time forward, firing every timer that comes due on the way, in order.</summary>
        public void Advance(TimeSpan _delta)
        {
            long target = nowTicks + _delta.Ticks;
            while (true)
            {
                var next = timers.Where(_t => _t.DueAt is { } due && due <= target).OrderBy(_t => _t.DueAt).FirstOrDefault();
                if (next is null)
                {
                    break;
                }
                nowTicks = next.DueAt!.Value;
                next.DueAt = next.Period > 0 ? nowTicks + next.Period : null;
                next.Fire();
            }
            nowTicks = target;
        }

        private sealed class FakeTimer : ITimer
        {
            private readonly FakeTimeProvider owner;
            private readonly TimerCallback callback;
            private readonly object? state;

            public FakeTimer(FakeTimeProvider _owner, TimerCallback _callback, object? _state)
            {
                owner = _owner;
                callback = _callback;
                state = _state;
            }

            public long? DueAt { get; set; }
            public long Period { get; private set; }

            public void Fire()
            {
                callback(state);
            }

            public bool Change(TimeSpan _dueTime, TimeSpan _period)
            {
                DueAt = _dueTime == Timeout.InfiniteTimeSpan ? null : owner.nowTicks + Math.Max(0, _dueTime.Ticks);
                Period = _period == Timeout.InfiniteTimeSpan ? 0 : _period.Ticks;
                return true;
            }

            public void Dispose()
            {
                DueAt = null;
                owner.timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
