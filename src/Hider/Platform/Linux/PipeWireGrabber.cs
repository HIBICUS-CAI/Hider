using System.Runtime.Versioning;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core;

namespace Hider.Platform.Linux
{
    /// <summary>
    /// Linux stage 2 (D-6, design §5.3): a ScreenCast session (persist_mode 2, restore token kept in ~/.hider) feeding
    /// libhidercap, which returns the first frame shown after the strip turned transparent (2 .. 3 frames instead of a
    /// 0.1 .. 0.3 s screenshot). The session is opened before a capture's first strip (<see cref="PrepareAsync"/>) and closed
    /// <see cref="IdleClose"/> after the last grab,
    /// so GNOME shows its screen sharing indicator only while Hider captures. Returns null (the next grabber takes over)
    /// when the helper is missing, the portal fails, or the area is not on the shared monitor.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public sealed class PipeWireGrabber : IScreenGrabber
    {
        public static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(3);

        /// <summary>After the portal or the helper failed, stage 1 is used this long before stage 2 is tried again.</summary>
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

        /// <summary>How long one strip waits for a new frame. Mutter sends frames on damage, and the transparent rows are damage.</summary>
        public const int GrabTimeoutMs = 1000;

        /// <summary>How long the prepare waits for a new session's first frame (a stream can take over a second to start).</summary>
        public const int FirstFrameTimeoutMs = 3000;

        private readonly IScreenCastPortal portal;
        private readonly IHidercap helper;
        private readonly RestoreTokenStore tokens;
        private readonly Func<IReadOnlyList<ScreenInfo>> screens;
        private readonly TimeProvider time;
        private readonly SemaphoreSlim gate = new(1, 1);
        private ScreenCastSession? session;
        private PhysicalRect? covers;
        private byte[] buffer = Array.Empty<byte>();
        private int frameWidth;
        private int frameHeight;
        private ITimer? idle;
        private long idleGeneration;
        private bool disposed;
        private bool refused;
        private long retryAt = long.MinValue;

        /// <summary>How long Dispose (on the UI thread at exit) waits for a running grab and for closing the session.</summary>
        public static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(3);

        /// <param name="_portal">Owned: disposed with the grabber.</param>
        /// <param name="_screens">Monitors in X11 physical px (safe to call on the capture thread).</param>
        public PipeWireGrabber(IScreenCastPortal _portal, IHidercap _helper, RestoreTokenStore _tokens, Func<IReadOnlyList<ScreenInfo>> _screens,
            TimeProvider? _time = null)
        {
            portal = _portal;
            helper = _helper;
            tokens = _tokens;
            screens = _screens;
            time = _time ?? TimeProvider.System;
        }

        /// <summary>Whether a session is open (for tests and diagnostics).</summary>
        public bool IsOpen => session is not null;

        public async Task<ScreenFrame?> GrabAfterAsync(long _notBeforeTimestamp, PhysicalRect _needed, CancellationToken _ct)
        {
            // Without the helper, after the user cancelled the dialog (asking again for every strip would be unbearable; the
            // next start of Hider asks again), or shortly after a failure, leave it to the next grabber.
            if (!helper.IsAvailable || refused || time.GetTimestamp() < retryAt)
            {
                return null;
            }
            await gate.WaitAsync(_ct).ConfigureAwait(false);
            try
            {
                CancelIdleClose();
                // Only PrepareAsync opens the session: here a strip is transparent, so no portal round trip or dialog may
                // start (closed by the idle timer, or stage 1 was prepared during a back-off): the strip just fails.
                if (disposed || session is null)
                {
                    return null;
                }
                if (covers is not { } shown || shown.Intersect(_needed) != _needed)
                {
                    Log.Verbose($"screencast: {_needed} is not on the shared monitor ({covers?.ToString() ?? "unknown"})");
                    return null;
                }
                long notBeforeNs = (long)(_notBeforeTimestamp * (1_000_000_000.0 / time.TimestampFrequency));
                var (code, info) = await Task.Run(() => Grab(notBeforeNs, shown, _needed, GrabTimeoutMs), CancellationToken.None).ConfigureAwait(false);
                _ct.ThrowIfCancellationRequested();
                switch (code)
                {
                    case Hidercap.Ok:
                        Log.Verbose($"screencast: frame {info.Width}x{info.Height} seq {info.Sequence}, {(notBeforeNs > 0 ? (info.PtsNs - notBeforeNs) / 1e6 : 0):F1} ms after the strip ({(info.PtsFromMeta != 0 ? "pts" : "receive time")})");
                        var frame = new FrameBuffer { Width = info.Width, Height = info.Height, Stride = info.Width * 4, Bgra = buffer };
                        return new ScreenFrame(frame, shown);
                    case Hidercap.Timeout:
                        Log.Verbose($"screencast: no frame within {GrabTimeoutMs} ms (last seq {info.Sequence})");
                        return null;
                    default:
                        Log.Verbose($"screencast: helper error {code}, closing the session");
                        await CloseAsync().ConfigureAwait(false);
                        BackOff();
                        return null;
                }
            }
            finally
            {
                ArmIdleClose();
                gate.Release();
            }
        }

        /// <summary>
        /// Opens the session (portal round trips, the first-time dialog, the stream start) and waits for its first frame
        /// before the coordinator makes any strip transparent, so none of that time shows uncovered content (NFR-05).
        /// </summary>
        public async Task<bool> PrepareAsync(PhysicalRect _needed, CancellationToken _ct)
        {
            if (!helper.IsAvailable || refused || time.GetTimestamp() < retryAt)
            {
                return false;
            }
            await gate.WaitAsync(_ct).ConfigureAwait(false);
            try
            {
                CancelIdleClose();
                if (disposed)
                {
                    return false;
                }
                if (session is null)
                {
                    if (!await OpenAsync(_ct).ConfigureAwait(false))
                    {
                        return false;
                    }
                    // Ready only once a frame came through; otherwise every strip would time out with its rows transparent.
                    var (code, _) = await Task.Run(() => Grab(0, null, default, FirstFrameTimeoutMs), CancellationToken.None).ConfigureAwait(false);
                    if (code != Hidercap.Ok)
                    {
                        Log.Verbose($"screencast: no first frame ({code}), closing the session");
                        await CloseAsync().ConfigureAwait(false);
                        BackOff();
                        return false;
                    }
                }
                return covers is { } shown && shown.Intersect(_needed) == _needed;
            }
            finally
            {
                ArmIdleClose();
                gate.Release();
            }
        }

        /// <summary>A session is open: strips can be grabbed without a prepare (they never open one, see GrabAfterAsync).</summary>
        public bool ReadyWithoutPrepare => session is not null && !disposed;

        /// <summary>Caller holds the gate. Each use of the session makes earlier idle timers stale (see CloseIdleAsync).</summary>
        private void CancelIdleClose()
        {
            idle?.Dispose();
            idle = null;
            idleGeneration++;
        }

        /// <summary>Caller holds the gate.</summary>
        private void ArmIdleClose()
        {
            if (session is not null && !disposed)
            {
                idle?.Dispose();
                long generation = ++idleGeneration;
                idle = time.CreateTimer(_ => CloseIdleAsync(generation).Forget("screencast: idle close"), null, IdleClose, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>The generation of the newest idle timer (tests).</summary>
        internal long IdleGeneration => idleGeneration;

        /// <summary>
        /// The helper call. Only the frame rows of <paramref name="_needed"/> (mapped into the frame like
        /// StripCoordinator.CopyRows does, one row of margin) are copied: a strip is 8 rows of a 2160-row monitor. All rows
        /// when <paramref name="_shown"/> is null or the frame size is not known yet; again with all rows when the size
        /// changed (the held frame is still recent enough, so that costs no wait). The buffer grows when needed.
        /// </summary>
        private (int Code, HidercapFrame Info) Grab(long _notBeforeNs, PhysicalRect? _shown, PhysicalRect _needed, int _timeoutMs)
        {
            var (row0, row1) = _shown is { } shown && frameHeight > 0 ? Rows(shown, _needed, frameHeight) : (0, int.MaxValue);
            int code = helper.GrabAfter(_notBeforeNs, _timeoutMs, row0, row1, buffer, out var info);
            if (code == Hidercap.BufferTooSmall)
            {
                buffer = new byte[(long)info.Width * info.Height * 4];
                code = helper.GrabAfter(_notBeforeNs, _timeoutMs, row0, row1, buffer, out info);
            }
            if (code == Hidercap.Ok && (info.Width != frameWidth || info.Height != frameHeight))
            {
                bool partial = row0 > 0 || row1 < info.Height;
                (frameWidth, frameHeight) = (info.Width, info.Height);
                if (partial)
                {
                    code = helper.GrabAfter(_notBeforeNs, _timeoutMs, 0, int.MaxValue, buffer, out info);
                }
            }
            return (code, info);
        }

        /// <summary>Frame rows holding <paramref name="_needed"/>'s rows when the frame shows <paramref name="_shown"/> in <paramref name="_frameHeight"/> rows.</summary>
        public static (int Row0, int Row1) Rows(PhysicalRect _shown, PhysicalRect _needed, int _frameHeight)
        {
            double sy = _shown.Height > 0 ? (double)_frameHeight / _shown.Height : 1.0;
            int row0 = (int)Math.Floor((_needed.Y + 0.5 - _shown.Y) * sy) - 1;
            int row1 = (int)Math.Floor((_needed.Bottom - 0.5 - _shown.Y) * sy) + 2;
            return (Math.Clamp(row0, 0, _frameHeight), Math.Clamp(row1, 0, _frameHeight));
        }

        private async Task<bool> OpenAsync(CancellationToken _ct)
        {
            ScreenCastSession? started;
            try
            {
                started = await portal.StartAsync(tokens.Load(), _ct).ConfigureAwait(false);
            }
            catch (CaptureDeniedException)
            {
                refused = true;
                throw;
            }
            if (started is null)
            {
                BackOff();
                return false;
            }
            // Single use: the token just spent is worthless, the new one (if any) replaces it (CT-91).
            tokens.Save(started.RestoreToken);
            int fd = (int)started.PipeWireFd.DangerousGetHandle();
            started.PipeWireFd.SetHandleAsInvalid(); // the helper owns the fd from here on, also when it fails
            int code = helper.Start(fd, started.NodeId);
            if (code != Hidercap.Ok)
            {
                Log.Verbose($"screencast: hidercap_start failed ({code})");
                await portal.CloseAsync(started).ConfigureAwait(false);
                BackOff();
                return false;
            }
            session = started;
            covers = MapStream(started.StreamRect, screens());
            return true;
        }

        private void BackOff()
        {
            retryAt = time.GetTimestamp() + (long)(RetryAfterFailure.TotalSeconds * time.TimestampFrequency);
        }

        /// <summary>
        /// The idle timer's callback. A timer that already fired may wait for the gate while a prepare or grab uses the
        /// session and arms a newer timer; it must then leave the session alone, or the capture's next strip finds it closed
        /// (and the area falls back to a generated pattern, FR-52).
        /// </summary>
        internal async Task CloseIdleAsync(long _generation)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_generation != idleGeneration)
                {
                    return;
                }
                await CloseAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Verbose($"screencast: idle close failed: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Caller holds the gate.</summary>
        private async Task CloseAsync()
        {
            idle?.Dispose();
            idle = null;
            if (session is null)
            {
                return;
            }
            var closing = session;
            session = null;
            covers = null;
            (frameWidth, frameHeight) = (0, 0);
            helper.Stop();
            await portal.CloseAsync(closing).ConfigureAwait(false);
        }

        /// <summary>
        /// The shared monitor in X11 physical px. The portal reports the stream's position in the compositor's (logical)
        /// layout; X11 sees either the same numbers or them times the monitor scale. Null when no monitor matches.
        /// </summary>
        public static PhysicalRect? MapStream(PhysicalRect _stream, IReadOnlyList<ScreenInfo> _screens)
        {
            // XWAYLAND-ASSUMPTION: X11 monitor origins equal the compositor's logical origins, possibly scaled.
            foreach (var screen in _screens)
            {
                if (screen.Bounds.X == _stream.X && screen.Bounds.Y == _stream.Y)
                {
                    return screen.Bounds;
                }
            }
            foreach (var screen in _screens)
            {
                if (screen.Bounds.X == (int)Math.Round(_stream.X * screen.Scale) && screen.Bounds.Y == (int)Math.Round(_stream.Y * screen.Scale))
                {
                    return screen.Bounds;
                }
            }
            return _screens.Count == 1 ? _screens[0].Bounds : null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            // Called on the UI thread at exit. A grab still holding the gate is being cancelled (its area was disposed first),
            // so the wait is short; it is bounded anyway, and the session is closed on the thread pool, where no await can
            // need the UI thread back and a hung D-Bus call cannot hold up the exit for long.
            bool entered = gate.Wait(DisposeTimeout);
            try
            {
                disposed = true;
                if (!Task.Run(CloseAsync).Wait(DisposeTimeout))
                {
                    Log.Verbose("screencast: closing the session at exit timed out");
                }
            }
            catch (AggregateException e)
            {
                Log.Verbose($"screencast: closing the session at exit failed: {e.InnerException?.Message}");
            }
            finally
            {
                // The gate is not disposed: a call waiting for it may get it from this Release and still release it
                // itself, and a SemaphoreSlim whose AvailableWaitHandle is never used owns nothing unmanaged.
                if (entered)
                {
                    gate.Release();
                }
            }
            portal.Dispose();
        }
    }
}
