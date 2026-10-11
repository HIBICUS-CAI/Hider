using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Persistence;

namespace Hider.Platform.Linux
{
    /// <summary>
    /// Linux stage 2 (D-6, design §5.3): a ScreenCast session (persist_mode 2, restore token kept in ~/.hider) feeding
    /// libhidercap, which returns the first frame shown after the strip turned transparent (2 .. 3 frames instead of a
    /// 0.1 .. 0.3 s screenshot). The session is opened for a capture and closed <see cref="IdleClose"/> after the last grab,
    /// so GNOME shows its screen sharing indicator only while Hider captures. Returns null (the next grabber takes over)
    /// when the helper is missing, the portal fails, or the area is not on the shared monitor.
    /// </summary>
    public sealed class PipeWireGrabber : IScreenGrabber
    {
        public static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(3);

        /// <summary>How long one strip waits for a new frame. Mutter sends frames on damage, and the transparent rows are damage.</summary>
        public const int GrabTimeoutMs = 1000;

        private readonly IScreenCastPortal portal;
        private readonly IHidercap helper;
        private readonly RestoreTokenStore tokens;
        private readonly Func<IReadOnlyList<ScreenInfo>> screens;
        private readonly TimeProvider time;
        private readonly SemaphoreSlim gate = new(1, 1);
        private ScreenCastSession? session;
        private PhysicalRect? covers;
        private byte[] buffer = Array.Empty<byte>();
        private ITimer? idle;
        private bool disposed;

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
            if (!helper.IsAvailable)
            {
                return null;
            }
            await gate.WaitAsync(_ct).ConfigureAwait(false);
            try
            {
                idle?.Dispose();
                idle = null;
                if (disposed || (session is null && !await OpenAsync(_ct).ConfigureAwait(false)))
                {
                    return null;
                }
                if (covers is not { } shown || shown.Intersect(_needed) != _needed)
                {
                    Log.Verbose($"screencast: {_needed} is not on the shared monitor ({covers?.ToString() ?? "unknown"})");
                    return null;
                }
                long notBeforeNs = (long)(_notBeforeTimestamp * (1_000_000_000.0 / time.TimestampFrequency));
                var (code, info) = await Task.Run(() => Grab(notBeforeNs), CancellationToken.None).ConfigureAwait(false);
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
                        return null;
                }
            }
            finally
            {
                if (session is not null && !disposed)
                {
                    idle = time.CreateTimer(_ => _ = CloseIdleAsync(), null, IdleClose, Timeout.InfiniteTimeSpan);
                }
                gate.Release();
            }
        }

        /// <summary>The helper call, growing the buffer once if the frame is larger than the last one.</summary>
        private (int Code, HidercapFrame Info) Grab(long _notBeforeNs)
        {
            int code = helper.GrabAfter(_notBeforeNs, GrabTimeoutMs, buffer, out var info);
            if (code == Hidercap.BufferTooSmall)
            {
                buffer = new byte[(long)info.Width * info.Height * 4];
                code = helper.GrabAfter(_notBeforeNs, GrabTimeoutMs, buffer, out info);
            }
            return (code, info);
        }

        private async Task<bool> OpenAsync(CancellationToken _ct)
        {
            var started = await portal.StartAsync(tokens.Load(), _ct).ConfigureAwait(false);
            if (started is null)
            {
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
                return false;
            }
            session = started;
            covers = MapStream(started.StreamRect, screens());
            return true;
        }

        private async Task CloseIdleAsync()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await CloseAsync().ConfigureAwait(false);
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
            gate.Wait();
            try
            {
                disposed = true;
                CloseAsync().GetAwaiter().GetResult();
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
