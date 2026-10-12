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
        /// Sets the timer interval. Nothing is captured because of the change itself (D-10: from the next timer). A longer
        /// interval applies after the tick already scheduled; a shorter one re-arms the timer to tick one new interval from
        /// now (otherwise going from an hour to 100 ms would wait up to the hour). 0 stops the timer; going from 0 to a value
        /// starts it from now.
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
                int previous = intervalMs;
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
                else if (interval < previous)
                {
                    timer.Change(TimeSpan.FromMilliseconds(interval), Timeout.InfiniteTimeSpan);
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
            Action<RefreshReason>? handler;
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
                // Read once: the run is marked for exactly the handler that gets invoked (and must call EndRun).
                handler = Due;
                running = handler is not null;
            }
            handler?.Invoke(_reason);
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
