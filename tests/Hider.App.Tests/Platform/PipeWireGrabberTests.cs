using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;
using Hider.Platform;
using Hider.Platform.Linux;
using Microsoft.Win32.SafeHandles;

namespace Hider.App.Tests.Platform
{
    public sealed class PipeWireGrabberTests : IDisposable
    {
        private static readonly ScreenInfo Primary = new(new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), 1.0, true);
        private static readonly ScreenInfo Right = new(new PhysicalRect(1920, 0, 2560, 1440), new PhysicalRect(1920, 0, 2560, 1400), 1.0, false);

        private readonly string dir = Path.Combine(Path.GetTempPath(), "hider-pw-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> log = new();
        private readonly FakeTimeProvider time = new();
        private readonly FakePortal portal;
        private readonly FakeHelper helper;
        private readonly RestoreTokenStore tokens;

        public PipeWireGrabberTests()
        {
            portal = new FakePortal(log);
            helper = new FakeHelper(log);
            tokens = new RestoreTokenStore(Path.Combine(dir, "restore_token"));
        }

        public void Dispose()
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        private sealed class FakePortal(List<string> _log) : IScreenCastPortal
        {
            public PhysicalRect StreamRect { get; set; } = new(0, 0, 1920, 1080);
            public Queue<string?> NewTokens { get; } = new();
            public List<string?> TokensUsed { get; } = new();
            public bool Deny { get; set; }
            public bool Missing { get; set; }

            public Task<ScreenCastSession?> StartAsync(string? _restoreToken, CancellationToken _ct)
            {
                _log.Add("portal start");
                TokensUsed.Add(_restoreToken);
                if (Deny)
                {
                    throw new CaptureDeniedException("test");
                }
                if (Missing)
                {
                    return Task.FromResult<ScreenCastSession?>(null);
                }
                string? token = NewTokens.Count > 0 ? NewTokens.Dequeue() : null;
                return Task.FromResult<ScreenCastSession?>(new ScreenCastSession("/session/1", 42, StreamRect, token, new SafeFileHandle(77, false)));
            }

            public Task CloseAsync(ScreenCastSession _session)
            {
                _log.Add("portal close");
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeHelper(List<string> _log) : IHidercap
        {
            public bool IsAvailable { get; set; } = true;
            public int StartResult { get; set; }
            public int GrabResult { get; set; }
            public (int W, int H) Size { get; set; } = (1920, 1080);
            public List<long> NotBeforeNs { get; } = new();
            private bool started;

            public int Start(int _fd, uint _nodeId)
            {
                _log.Add($"start {_fd} {_nodeId}");
                Assert.False(started, "hidercap_start twice without stop");
                started = StartResult == 0;
                return StartResult;
            }

            public int GrabAfter(long _notBeforeNs, int _timeoutMs, Span<byte> _dst, out HidercapFrame _frame)
            {
                _log.Add("grab");
                Assert.True(started, "grab before start");
                NotBeforeNs.Add(_notBeforeNs);
                _frame = new HidercapFrame { Width = Size.W, Height = Size.H, Stride = Size.W * 4, PtsFromMeta = 1, PtsNs = _notBeforeNs + 16_000_000, Sequence = 3 };
                if (GrabResult != 0)
                {
                    return GrabResult;
                }
                if (_dst.Length < Size.W * Size.H * 4)
                {
                    return Hidercap.BufferTooSmall;
                }
                _dst[0] = 0x5A;
                return Hidercap.Ok;
            }

            public ulong LastSequence() => 3;

            public void Stop()
            {
                _log.Add("stop");
                started = false;
            }
        }

        private PipeWireGrabber Create(params ScreenInfo[] _screens)
        {
            var screens = _screens.Length == 0 ? new[] { Primary } : _screens;
            return new PipeWireGrabber(portal, helper, tokens, () => screens, time);
        }

        private static Task<ScreenFrame?> Grab(PipeWireGrabber _g, long _notBefore = 0, PhysicalRect? _needed = null)
        {
            return _g.GrabAfterAsync(_notBefore, _needed ?? new PhysicalRect(100, 100, 200, 100), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CT89_StartGrabsStop_NoDoubleStart()
        {
            using var g = Create();
            for (int i = 0; i < 3; i++)
            {
                var frame = await Grab(g);
                Assert.NotNull(frame);
                Assert.Equal((1920, 1080), (frame!.Frame.Width, frame.Frame.Height));
                Assert.Equal(0x5A, frame.Frame.Bgra.Span[0]);
                Assert.Equal(new PhysicalRect(0, 0, 1920, 1080), frame.Covers);
            }
            // The first grab grows the buffer and asks again; the session stays open between grabs.
            Assert.Equal(new[] { "portal start", "start 77 42", "grab", "grab", "grab", "grab" }, log);

            // Closed after the idle time (the screen sharing indicator goes away), opened again for the next capture.
            time.Advance(PipeWireGrabber.IdleClose);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(g.IsOpen);
            Assert.Equal(new[] { "stop", "portal close" }, log.TakeLast(2));
            Assert.NotNull(await Grab(g));
            Assert.Equal(new[] { "portal start", "start 77 42", "grab" }, log.TakeLast(3));
        }

        [Fact]
        public async Task CT90_NotBefore_ConvertedToNanoseconds_TimeoutIsNull()
        {
            using var g = Create();
            // FakeTimeProvider counts 100 ns ticks: 12345 ticks = 1 234 500 ns.
            await Grab(g, 12345);
            Assert.Equal(1_234_500, helper.NotBeforeNs[^1]);

            helper.GrabResult = Hidercap.Timeout;
            Assert.Null(await Grab(g, 20000));
            Assert.True(g.IsOpen, "a timeout keeps the session");

            helper.GrabResult = Hidercap.StreamError;
            Assert.Null(await Grab(g));
            Assert.False(g.IsOpen, "a stream error closes it");
            Assert.Equal(new[] { "stop", "portal close" }, log.TakeLast(2));
        }

        [Fact]
        public async Task CT91_RestoreToken_UsedAndReplaced()
        {
            portal.NewTokens.Enqueue("t1");
            portal.NewTokens.Enqueue("t2");
            portal.NewTokens.Enqueue(null);
            using var g = Create();
            await Grab(g);
            Assert.Equal("t1", tokens.Load());
            for (int i = 0; i < 2; i++)
            {
                time.Advance(PipeWireGrabber.IdleClose);
                await Task.Delay(50, TestContext.Current.CancellationToken);
                await Grab(g);
            }
            // Absent at first (dialog), then each start spends the previous token; no new token leaves none.
            Assert.Equal(new string?[] { null, "t1", "t2" }, portal.TokensUsed);
            Assert.Null(tokens.Load());
        }

        [Fact]
        public async Task CT92_NoHelper_NoPortalCall()
        {
            helper.IsAvailable = false;
            using var g = Create();
            Assert.Null(await Grab(g));
            Assert.Empty(log);
        }

        [Fact]
        public async Task PortalMissing_NullAndNoRetryForAWhile()
        {
            portal.Missing = true;
            using var g = Create();
            Assert.Null(await Grab(g));
            Assert.Null(await Grab(g)); // the next strips go straight to stage 1
            Assert.Single(log, "portal start");
            portal.Missing = false;
            time.Advance(PipeWireGrabber.RetryAfterFailure);
            Assert.NotNull(await Grab(g));
        }

        [Fact]
        public async Task HelperStartFails_SessionClosed_NoRetryForAWhile()
        {
            helper.StartResult = Hidercap.StreamError;
            using var g = Create();
            Assert.Null(await Grab(g));
            Assert.Equal("portal close", log[^1]);
            Assert.Null(await Grab(g));
            Assert.Single(log, "portal start");
        }

        [Fact]
        public async Task Denied_PropagatesOnce_ThenNeverAsksAgain()
        {
            portal.Deny = true;
            using var g = Create();
            await Assert.ThrowsAsync<CaptureDeniedException>(() => Grab(g));
            time.Advance(TimeSpan.FromHours(1));
            Assert.Null(await Grab(g));
            Assert.Single(log, "portal start");
        }

        [Fact]
        public async Task AreaOnAnotherMonitor_IsNull()
        {
            portal.StreamRect = new PhysicalRect(1920, 0, 2560, 1440);
            using var g = Create(Primary, Right);
            Assert.Null(await Grab(g, 0, new PhysicalRect(100, 100, 200, 100)));
            var frame = await Grab(g, 0, new PhysicalRect(2000, 100, 200, 100));
            Assert.Equal(Right.Bounds, frame!.Covers);
        }

        [Theory]
        [InlineData(0, 0, 0)]       // same numbers in X11 and the portal
        [InlineData(1920, 0, 1)]
        [InlineData(1280, 0, 1)]    // logical 1280 = X11 1920 at scale 1.5
        [InlineData(500, 500, -1)]  // nothing there
        public void MapStream_FindsTheMonitor(int _x, int _y, int _expected)
        {
            var screens = new[]
            {
                new ScreenInfo(new PhysicalRect(0, 0, 1920, 1080), default, 1.0, true),
                new ScreenInfo(new PhysicalRect(1920, 0, 3840, 2160), default, 1.5, false),
            };
            var mapped = PipeWireGrabber.MapStream(new PhysicalRect(_x, _y, 100, 100), screens);
            Assert.Equal(_expected < 0 ? null : screens[_expected].Bounds, mapped);
            Assert.Equal(screens[0].Bounds, PipeWireGrabber.MapStream(new PhysicalRect(9, 9, 1, 1), screens.Take(1).ToList()));
        }

        [Fact]
        public async Task Prepare_OpensTheSessionAndWaitsForAFrame_BeforeAnyStrip()
        {
            portal.StreamRect = new PhysicalRect(1920, 0, 2560, 1440);
            using var g = Create(Primary, Right);
            var ct = TestContext.Current.CancellationToken;
            Assert.True(await g.PrepareAsync(new PhysicalRect(2000, 100, 200, 100), ct));
            Assert.Equal(new[] { "portal start", "start 77 42", "grab", "grab" }, log);
            Assert.True(g.IsOpen);
            // Already open: nothing more to do. An area on the other monitor is not this grabber's.
            log.Clear();
            Assert.False(await g.PrepareAsync(new PhysicalRect(100, 100, 200, 100), ct));
            Assert.Empty(log);
        }

        [Fact]
        public async Task Prepare_Denied_RefusesFromThenOn()
        {
            portal.Deny = true;
            using var g = Create();
            var ct = TestContext.Current.CancellationToken;
            await Assert.ThrowsAsync<CaptureDeniedException>(() => g.PrepareAsync(new PhysicalRect(1, 1, 10, 10), ct));
            Assert.False(await g.PrepareAsync(new PhysicalRect(1, 1, 10, 10), ct));
            Assert.Single(log, "portal start");
        }
    }
}
