namespace Hider.Core.Scheduling
{
    /// <summary>Why a capture is requested (FR-50).</summary>
    public enum RefreshReason
    {
        Created,
        Committed,
        Manual,
        Timer,
    }

    /// <summary>
    /// Decides when an area's underlay is captured again (FR-50, FR-51, D-10). <see cref="Due"/> is raised immediately for
    /// requests and periodically for the timer (on the timer's thread). Raising <see cref="Due"/> starts a run: until the
    /// handler's work calls <see cref="EndRun"/>, further requests and ticks are merged into one, raised after it. The run
    /// starts when Due is raised, not when the handler gets to it (it may be posted to another thread), so two captures of
    /// one area never overlap.
    /// </summary>
    public sealed class RefreshScheduler : IDisposable
    {
        private readonly TimeProvider time;
        private readonly object gate = new();
        private ITimer? timer;
        private int intervalMs;
        private bool running;
        private RefreshReason? pending;
        private bool timerPaused;
        private bool disposed;

        public RefreshScheduler(TimeProvider _time)
        {
            time = _time;
        }

        public event Action<RefreshReason>? Due;

        /// <summary>Current interval; 0 means no timer (FR-51).</summary>
        public int IntervalMs
        {
            get
            {
                lock (gate)
                {
                    return intervalMs;
                }
            }
        }

        /// <summary>
        /// Sets the timer interval. A running period is not cut short: the next tick still comes on the old schedule and the
        /// new interval applies from there (D-10). 0 stops the timer; going from 0 to a value starts it from now.
        /// </summary>
        public void Configure(int _intervalMs)
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
                int interval = Math.Max(0, _intervalMs);
                if (interval == intervalMs)
                {
                    return;
                }
                intervalMs = interval;
                if (interval == 0)
                {
                    timer?.Dispose();
                    timer = null;
                    return;
                }
                if (timer is null)
                {
                    timer = time.CreateTimer(_ => OnTick(), null, TimeSpan.FromMilliseconds(interval), Timeout.InfiniteTimeSpan);
                }
                // Otherwise OnTick re-arms with the new interval after the tick already scheduled.
            }
        }

        /// <summary>
        /// Pauses timer ticks while an area is dragged with a capture that cannot follow live (FR-38: Windows keeps updating
        /// during a drag, Linux captures only when the drag is committed). Requests still go through.
        /// </summary>
        public bool TimerPaused
        {
            get
            {
                lock (gate)
                {
                    return timerPaused;
                }
            }
            set
            {
                lock (gate)
                {
                    timerPaused = value;
                }
            }
        }

        /// <summary>Asks for a capture now (or right after the running one).</summary>
        public void Request(RefreshReason _reason)
        {
            Raise(_reason);
        }

        /// <summary>
        /// Ends the run that the last <see cref="Due"/> started (also when the handler decided not to capture) and raises the
        /// merged request, if any.
        /// </summary>
        public void EndRun()
        {
            RefreshReason? next;
            lock (gate)
            {
                running = false;
                next = pending;
                pending = null;
            }
            if (next is { } reason)
            {
                Raise(reason);
            }
        }

        private void OnTick()
        {
            bool fire;
            lock (gate)
            {
                if (disposed || timer is null || intervalMs == 0)
                {
                    return;
                }
                // Next tick on the current interval (a Configure since the last tick takes effect here).
                timer.Change(TimeSpan.FromMilliseconds(intervalMs), Timeout.InfiniteTimeSpan);
                fire = !timerPaused;
            }
            if (fire)
            {
                Raise(RefreshReason.Timer);
            }
        }

        private void Raise(RefreshReason _reason)
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
                if (running)
                {
                    // Keep the most specific reason: anything beats a timer tick.
                    if (pending is null || pending == RefreshReason.Timer)
                    {
                        pending = _reason;
                    }
                    return;
                }
                running = Due is not null;
            }
            Due?.Invoke(_reason);
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                timer?.Dispose();
                timer = null;
                pending = null;
            }
        }
    }
}
