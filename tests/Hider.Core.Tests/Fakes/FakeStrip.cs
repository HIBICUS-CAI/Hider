using Hider.Core.Capture;
using Hider.Core.Model;

namespace Hider.Core.Tests.Fakes
{
    /// <summary>A screen whose pixel (x, y) encodes its coordinates: B = x, G = y, R = (x &gt;&gt; 8) | (y &gt;&gt; 8) &lt;&lt; 4.</summary>
    public static class CoordinateFrame
    {
        public static FrameBuffer Create(int _width, int _height)
        {
            var frame = FrameBuffer.Allocate(_width, _height);
            var span = frame.Bgra.Span;
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int i = y * frame.Stride + x * 4;
                    span[i] = (byte)x;
                    span[i + 1] = (byte)y;
                    span[i + 2] = (byte)((x >> 8) | ((y >> 8) << 4));
                    span[i + 3] = 255;
                }
            }
            return frame;
        }

        public static (int X, int Y) Decode(FrameBuffer _frame, int _x, int _y)
        {
            var span = _frame.Bgra.Span;
            int i = _y * _frame.Stride + _x * 4;
            return (span[i] | ((span[i + 2] & 0xF) << 8), span[i + 1] | ((span[i + 2] >> 4) << 8));
        }
    }

    /// <summary>Records every call in one log shared with <see cref="FakeStripHost"/>.</summary>
    public sealed class FakeScreenGrabber : IScreenGrabber
    {
        private readonly List<string> log;
        private int running;

        public FakeScreenGrabber(List<string> _log, FrameBuffer? _frame)
        {
            log = _log;
            Frame = _frame;
        }

        public FrameBuffer? Frame { get; set; }

        /// <summary>What the frame shows; default: the frame's own size at the origin.</summary>
        public PhysicalRect? Covers { get; set; }

        public List<PhysicalRect> Needed { get; } = new();
        public bool Deny { get; set; }

        /// <summary>Fail (return null) on this call number (1-based); 0 = never.</summary>
        public int FailOnCall { get; set; }

        /// <summary>Wait this long inside each grab (real time), to expose overlapping calls.</summary>
        public TimeSpan Delay { get; set; }

        /// <summary>Never completes until cancelled.</summary>
        public bool Hang { get; set; }

        /// <summary>Called inside each grab (e.g. to look at the windows while their strip is transparent).</summary>
        public Action? OnGrab { get; set; }

        /// <summary>What <see cref="PrepareAsync"/> answers; DenyPrepare makes it throw a refusal.</summary>
        public bool Ready { get; set; } = true;

        /// <summary>What <see cref="IScreenGrabber.ReadyWithoutPrepare"/> answers.</summary>
        public bool ReadyWithoutPrepare { get; set; }
        public bool DenyPrepare { get; set; }

        public Task<bool> PrepareAsync(PhysicalRect _needed, CancellationToken _ct)
        {
            lock (log)
            {
                log.Add("prepare");
            }
            return DenyPrepare ? throw new CaptureDeniedException("test") : Task.FromResult(Ready);
        }

        public int Calls { get; private set; }
        public int MaxConcurrent { get; private set; }
        public List<long> NotBefore { get; } = new();

        public async Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct)
        {
            int now = Interlocked.Increment(ref running);
            try
            {
                lock (log)
                {
                    Calls++;
                    MaxConcurrent = Math.Max(MaxConcurrent, now);
                    NotBefore.Add(_notBeforeTimestamp);
                    Needed.Add(_needed);
                    log.Add("grab");
                }
                OnGrab?.Invoke();
                if (Hang)
                {
                    await Task.Delay(Timeout.Infinite, _ct);
                }
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, _ct);
                }
                if (Deny)
                {
                    throw new CaptureDeniedException("test");
                }
                if (Calls == FailOnCall || Frame is null)
                {
                    return null;
                }
                return new ScreenFrame(Frame, Covers ?? new PhysicalRect(0, 0, Frame.Width, Frame.Height));
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }

        public void Dispose()
        {
        }
    }

    public sealed class FakeStripHost : IStripHost
    {
        private readonly List<string> log;
        private readonly FakeTimeProvider? time;

        /// <param name="_time">Advanced by 5 ms per call, as if drawing took time; records when each finished.</param>
        public FakeStripHost(List<string> _log, FakeTimeProvider? _time = null)
        {
            log = _log;
            time = _time;
        }

        public HashSet<string> Transparent { get; } = new();
        public List<long> SetCompletedAt { get; } = new();

        public Task SetTransparentRowsAsync(string _areaId, int _y0, int _y1, CancellationToken _ct)
        {
            _ct.ThrowIfCancellationRequested();
            time?.Advance(TimeSpan.FromMilliseconds(5));
            lock (log)
            {
                log.Add($"set {_areaId} {_y0}-{_y1}");
                Transparent.Add(_areaId);
                SetCompletedAt.Add(time?.GetTimestamp() ?? 0);
            }
            return Task.CompletedTask;
        }

        public Task RestoreAsync(string _areaId)
        {
            lock (log)
            {
                log.Add($"restore {_areaId}");
                Transparent.Remove(_areaId);
            }
            return Task.CompletedTask;
        }
    }
}
