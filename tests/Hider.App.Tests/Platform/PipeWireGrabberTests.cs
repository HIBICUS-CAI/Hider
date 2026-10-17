using System.Runtime.Versioning;
using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;
using Hider.Platform;
using Hider.Platform.Linux;
using Microsoft.Win32.SafeHandles;

namespace Hider.App.Tests.Platform
{
    // Linux-only classes with fakes (no PipeWire, no portal): the tests run on every OS.
    [SupportedOSPlatform("linux")]
    public sealed class PipeWireGrabberTests : IDisposable
    {
        private static readonly string[] prepareThenThreeGrabs = { "portal start", "start 77 42", "grab", "grab", "grab", "grab", "grab" };
        private static readonly string[] stopAndClose = { "stop", "portal close" };
        private static readonly string[] openAndFirstGrab = { "portal start", "start 77 42", "grab", "grab" };
        private static readonly string[] stopCloseDispose = { "stop", "portal close", "portal dispose" };
        private static readonly string[] noFirstFrame = { "portal start", "start 77 42", "grab", "stop", "portal close" };

        private static readonly ScreenInfo primary = new(new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), 1.0, true);
        private static readonly ScreenInfo right = new(new PhysicalRect(1920, 0, 2560, 1440), new PhysicalRect(1920, 0, 2560, 1400), 1.0, false);

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

            /// <summary>What closing does after it is logged (default: done at once).</summary>
            public Func<Task>? OnClose { get; set; }

            public Task CloseAsync(ScreenCastSession _session)
            {
                _log.Add("portal close");
                return OnClose?.Invoke() ?? Task.CompletedTask;
            }

            public void Dispose()
            {
                _log.Add("portal dispose");
            }
        }

        private sealed class FakeHelper(List<string> _log) : IHidercap
        {
            public bool IsAvailable { get; set; } = true;
            public int StartResult { get; set; }
            public int GrabResult { get; set; }
            public (int W, int H) Size { get; set; } = (1920, 1080);
            public int PtsFromMeta { get; set; } = 1;
            public List<long> NotBeforeNs { get; } = new();
            public List<(int Row0, int Row1)> Rows { get; } = new();
            public List<int> TimeoutsMs { get; } = new();
            /// <summary>When set, a grab blocks until it is signalled (a grab still running in the helper).</summary>
            public ManualResetEventSlim? Block { get; set; }
            public ManualResetEventSlim Entered { get; } = new();
            private bool started;

            public int Start(int _fd, uint _nodeId)
            {
                _log.Add($"start {_fd} {_nodeId}");
                Assert.False(started, "hidercap_start twice without stop");
                started = StartResult == 0;
                return StartResult;
            }

            public int GrabAfter(long _notBeforeNs, int _timeoutMs, int _row0, int _row1, Span<byte> _dst, out HidercapFrame _frame)
            {
                _log.Add("grab");
                Assert.True(started, "grab before start");
                Entered.Set();
                Block?.Wait();
                NotBeforeNs.Add(_notBeforeNs);
                Rows.Add((_row0, _row1));
                TimeoutsMs.Add(_timeoutMs);
                _frame = new HidercapFrame { Width = Size.W, Height = Size.H, Stride = Size.W * 4, PtsFromMeta = PtsFromMeta, PtsNs = _notBeforeNs + 16_000_000, Sequence = 3 };
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

            public void StopStream()
            {
                _log.Add("stop");
                started = false;
            }
        }

        private PipeWireGrabber Create(params ScreenInfo[] _screens)
        {
            var screens = _screens.Length == 0 ? new[] { primary } : _screens;
            return new PipeWireGrabber(portal, helper, tokens, () => screens, time);
        }

        private static Task<ScreenFrame?> Grab(PipeWireGrabber _g, long _notBefore = 0, PhysicalRect? _needed = null)
        {
            return _g.GrabAfterAsync(_notBefore, _needed ?? defaultNeeded, TestContext.Current.CancellationToken);
        }

        private static readonly PhysicalRect defaultNeeded = new(100, 100, 200, 100);

        private static Task<bool> Prepare(PipeWireGrabber _g, PhysicalRect? _needed = null)
        {
            return _g.PrepareAsync(_needed ?? defaultNeeded, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CT89_StartGrabsStop_NoDoubleStart()
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            for (int i = 0; i < 3; i++)
            {
                var frame = await Grab(g);
                Assert.NotNull(frame);
                Assert.Equal((1920, 1080), (frame!.Frame.Width, frame.Frame.Height));
                Assert.Equal(0x5A, frame.Frame.Bgra.Span[0]);
                Assert.Equal(new PhysicalRect(0, 0, 1920, 1080), frame.Covers);
            }
            // The first grab (in the prepare) grows the buffer and asks again; the session stays open between grabs.
            Assert.Equal(prepareThenThreeGrabs, log);

            // Closed after the idle time (the screen sharing indicator goes away), opened again by the next prepare. The
            // fake clock runs the timer and the close synchronously.
            time.Advance(PipeWireGrabber.IdleClose);
            Assert.False(g.IsOpen);
            Assert.Equal(stopAndClose, log.TakeLast(2));
            Assert.True(await Prepare(g));
            Assert.NotNull(await Grab(g));
            Assert.Equal(openAndFirstGrab, log.TakeLast(4));
        }

        [Fact]
        public async Task Dispose_ClosesTheSessionAndDisposesThePortal_Once()
        {
            var g = Create();
            Assert.True(await Prepare(g));
            g.Dispose();
            g.Dispose();
            Assert.Equal(stopCloseDispose, log.TakeLast(3));
            Assert.Single(log, "portal dispose");
            Assert.False(g.IsOpen);
        }

        [Fact]
        public async Task Dispose_WhileAGrabRuns_LeavesTheHelperAlone()
        {
            // The first frame of a prepare waits in the helper and holds the gate; the helper takes no concurrent calls, so
            // Dispose (at exit) must not stop the stream under it.
            var g = new PipeWireGrabber(portal, helper, tokens, () => new[] { primary }, time) { DisposeWait = TimeSpan.FromMilliseconds(50) };
            using var block = new ManualResetEventSlim();
            helper.Block = block;
            var prepare = Prepare(g);
            Assert.True(helper.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            g.Dispose();
            Assert.DoesNotContain("stop", log);
            Assert.DoesNotContain("portal close", log);
            Assert.Contains("portal dispose", log);
            block.Set();
            await prepare;
        }

        [Fact]
        public async Task StaleIdleTimer_LeavesTheSessionOfALaterCaptureOpen()
        {
            // The idle timer fires as the next capture's prepare takes the gate: its callback waits, the prepare finds the
            // session open and arms a new timer, and then the old callback runs. It must not close the session the capture's
            // strips are about to use.
            using var g = Create();
            Assert.True(await Prepare(g));
            long fired = g.IdleGeneration;
            Assert.True(await Prepare(g));
            await g.CloseIdleAsync(fired);
            Assert.True(g.IsOpen);
            Assert.NotNull(await Grab(g));
            // The newest timer still closes it.
            await g.CloseIdleAsync(g.IdleGeneration);
            Assert.False(g.IsOpen);
        }

        [Fact]
        public async Task Prepare_WaitsLongerForTheFirstFrame()
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            Assert.Equal(PipeWireGrabber.FirstFrameTimeoutMs, helper.TimeoutsMs[0]);
            await Grab(g);
            Assert.Equal(PipeWireGrabber.GrabTimeoutMs, helper.TimeoutsMs[^1]);
        }

        [Fact]
        public async Task Grab_NeverOpensTheSession()
        {
            // A strip is transparent during a grab: a session closed in between (idle timer) or never prepared is not
            // opened there (portal round trips, maybe a dialog); the strip fails instead.
            using var g = Create();
            Assert.Null(await Grab(g));
            Assert.Empty(log);
            Assert.True(await Prepare(g));
            time.Advance(PipeWireGrabber.IdleClose);
            log.Clear();
            Assert.Null(await Grab(g));
            Assert.Empty(log);
        }

        [Fact]
        public async Task Prepare_NoFirstFrame_NotReady_SessionClosed()
        {
            helper.GrabResult = Hidercap.Timeout;
            using var g = Create();
            Assert.False(await Prepare(g));
            Assert.False(g.IsOpen);
            Assert.Equal(noFirstFrame, log);
            // Backed off: stage 1 is used for a while instead of opening the portal for every capture.
            helper.GrabResult = Hidercap.Ok;
            Assert.False(await Prepare(g));
            Assert.Single(log, "portal start");
            time.Advance(PipeWireGrabber.RetryAfterFailure);
            Assert.True(await Prepare(g));
        }

        [Fact]
        public async Task NotBefore_ConvertedToNanoseconds_TimeoutIsNull()
        {
            // The C# side of CT-90 only: the helper is given notBefore in CLOCK_MONOTONIC ns. Which frames are old enough to
            // skip is decided in libhidercap and tested there (native/hidercap/test_frame_time.c, "make test" in CI).
            using var g = Create();
            Assert.True(await Prepare(g));
            // FakeTimeProvider counts 100 ns ticks: 12345 ticks = 1 234 500 ns.
            await Grab(g, 12345);
            Assert.Equal(1_234_500, helper.NotBeforeNs[^1]);

            helper.GrabResult = Hidercap.Timeout;
            Assert.Null(await Grab(g, 20000));
            Assert.True(g.IsOpen, "a timeout keeps the session");

            helper.GrabResult = Hidercap.StreamError;
            Assert.Null(await Grab(g));
            Assert.False(g.IsOpen, "a stream error closes it");
            Assert.Equal(stopAndClose, log.TakeLast(2));
        }

        [Fact]
        public async Task CT91_RestoreToken_UsedAndReplaced()
        {
            portal.NewTokens.Enqueue("t1");
            portal.NewTokens.Enqueue("t2");
            portal.NewTokens.Enqueue(null);
            using var g = Create();
            Assert.True(await Prepare(g));
            Assert.Equal("t1", tokens.Load());
            for (int i = 0; i < 2; i++)
            {
                time.Advance(PipeWireGrabber.IdleClose);
                Assert.True(await Prepare(g));
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
            Assert.False(await Prepare(g));
            Assert.Null(await Grab(g));
            Assert.Empty(log);
        }

        [Fact]
        public async Task PortalMissing_NotReadyAndNoRetryForAWhile()
        {
            portal.Missing = true;
            using var g = Create();
            Assert.False(await Prepare(g));
            Assert.False(await Prepare(g)); // the next captures go straight to stage 1
            Assert.Single(log, "portal start");
            portal.Missing = false;
            time.Advance(PipeWireGrabber.RetryAfterFailure);
            Assert.True(await Prepare(g));
        }

        [Fact]
        public async Task HelperStartFails_SessionClosed_NoRetryForAWhile()
        {
            helper.StartResult = Hidercap.StreamError;
            using var g = Create();
            Assert.False(await Prepare(g));
            Assert.Equal("portal close", log[^1]);
            Assert.False(await Prepare(g));
            Assert.Single(log, "portal start");
        }

        [Fact]
        public async Task Denied_PropagatesOnce_ThenNeverAsksAgain()
        {
            portal.Deny = true;
            using var g = Create();
            await Assert.ThrowsAsync<CaptureDeniedException>(() => Prepare(g));
            time.Advance(TimeSpan.FromHours(1));
            Assert.False(await Prepare(g));
            Assert.Single(log, "portal start");
        }

        [Fact]
        public async Task AreaOnAnotherMonitor_IsNull()
        {
            portal.StreamRect = new PhysicalRect(1920, 0, 2560, 1440);
            using var g = Create(primary, right);
            Assert.True(await Prepare(g, new PhysicalRect(2000, 100, 200, 100)));
            Assert.Null(await Grab(g, 0, new PhysicalRect(100, 100, 200, 100)));
            var frame = await Grab(g, 0, new PhysicalRect(2000, 100, 200, 100));
            Assert.Equal(right.Bounds, frame!.Covers);
        }

        [Fact]
        public async Task Grab_CopiesOnlyTheNeededRows()
        {
            // A strip of 8 rows needs 8 rows (plus a row of margin each side) of the monitor, not all 1080.
            using var g = Create();
            Assert.True(await Prepare(g));
            Assert.Equal((0, int.MaxValue), helper.Rows[^1]); // the first frame: size unknown, all rows
            await Grab(g, 0, new PhysicalRect(100, 300, 200, 8));
            Assert.Equal((299, 309), helper.Rows[^1]);

            // The frame size changed (monitor mode switch): asked again for all rows of the new frame.
            helper.Size = (3840, 2160);
            int calls = helper.Rows.Count;
            var frame = await Grab(g, 0, new PhysicalRect(100, 300, 200, 8));
            Assert.Equal((3840, 2160), (frame!.Frame.Width, frame.Frame.Height));
            Assert.Equal((0, int.MaxValue), helper.Rows[^1]);
            await Grab(g, 0, new PhysicalRect(100, 300, 200, 8));
            Assert.Equal((600, 617), helper.Rows[^1]); // the frame is scaled 2x against the monitor's 1920 x 1080
            Assert.True(helper.Rows.Count > calls);
        }

        [Theory]
        [InlineData(0, 0, 1080, 300, 8, 299, 309)]
        [InlineData(0, 0, 2160, 300, 8, 600, 617)]
        [InlineData(0, 0, 1080, 0, 8, 0, 9)]
        [InlineData(0, 0, 1080, 1076, 4, 1075, 1080)]
        [InlineData(0, 100, 1080, 150, 8, 49, 59)]
        public void Rows_CoverWhatCopyRowsReads(int _shownX, int _shownY, int _frameHeight, int _y, int _h, int _row0, int _row1)
        {
            var shown = new PhysicalRect(_shownX, _shownY, 1920, 1080);
            var needed = new PhysicalRect(10, _y, 50, _h);
            Assert.Equal((_row0, _row1), PipeWireGrabber.Rows(shown, needed, _frameHeight));
            // Every row StripCoordinator.CopyRows samples lies inside.
            double sy = (double)_frameHeight / shown.Height;
            for (int y = needed.Y; y < needed.Bottom; y++)
            {
                int fy = (int)Math.Floor((y + 0.5 - shown.Y) * sy);
                Assert.InRange(fy, _row0, _row1 - 1);
            }
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
            using var g = Create(primary, right);
            var ct = TestContext.Current.CancellationToken;
            Assert.True(await g.PrepareAsync(new PhysicalRect(2000, 100, 200, 100), ct));
            Assert.Equal(openAndFirstGrab, log); // the first grab grows the buffer
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

        [Fact]
        public void Constants_AreTheDesignValues()
        {
            Assert.Equal(TimeSpan.FromSeconds(3), PipeWireGrabber.IdleClose);
            Assert.Equal(TimeSpan.FromMinutes(1), PipeWireGrabber.RetryAfterFailure);
            Assert.Equal(1000, PipeWireGrabber.GrabTimeoutMs);
            Assert.Equal(3000, PipeWireGrabber.FirstFrameTimeoutMs);
            Assert.Equal(TimeSpan.FromSeconds(3), PipeWireGrabber.DisposeTimeout);
            using var g = Create();
            Assert.Equal(PipeWireGrabber.DisposeTimeout, g.DisposeWait);
        }

        [Fact]
        public async Task WithoutAClock_TheSystemClockIsUsed()
        {
            using var g = new PipeWireGrabber(portal, helper, tokens, () => new[] { primary });
            Assert.True(await Prepare(g));
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            Assert.NotNull(await g.GrabAfterAsync(now, defaultNeeded, TestContext.Current.CancellationToken));
            // The system clock's timestamps, converted to ns.
            Assert.Equal((long)(now * (1_000_000_000.0 / System.Diagnostics.Stopwatch.Frequency)), helper.NotBeforeNs[^1]);
        }

        [Fact]
        public async Task ReadyWithoutPrepare_OnlyWhileASessionIsOpen()
        {
            var g = Create();
            Assert.False(g.ReadyWithoutPrepare);
            Assert.True(await Prepare(g));
            Assert.True(g.ReadyWithoutPrepare);
            time.Advance(PipeWireGrabber.IdleClose);
            Assert.False(g.ReadyWithoutPrepare);
            Assert.True(await Prepare(g));
            g.Dispose();
            Assert.False(g.ReadyWithoutPrepare);
        }

        [Fact]
        public async Task AfterDispose_PrepareAndGrabDoNothing()
        {
            var g = Create();
            g.Dispose();
            log.Clear();
            Assert.False(await Prepare(g));
            Assert.Null(await Grab(g));
            Assert.Empty(log);
            Assert.False(g.IsOpen);
        }

        [Fact]
        public async Task Dispose_WhileAGrabRuns_TheSessionStaysButIsNotReady_AndNoIdleTimer()
        {
            var g = new PipeWireGrabber(portal, helper, tokens, () => new[] { primary }, time) { DisposeWait = TimeSpan.FromMilliseconds(50) };
            using var block = new ManualResetEventSlim();
            helper.Block = block;
            var prepare = Prepare(g);
            Assert.True(helper.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                g.Dispose();
            }
            Assert.Contains("a grab is still running at exit", captured.ToString(), StringComparison.Ordinal);
            block.Set();
            await prepare;
            // Left to the process exit: open, but never offered again, and no idle timer to close it later.
            Assert.True(g.IsOpen);
            Assert.False(g.ReadyWithoutPrepare);
            long generation = g.IdleGeneration;
            time.Advance(PipeWireGrabber.IdleClose * 2);
            Assert.Equal(generation, g.IdleGeneration);
            Assert.DoesNotContain("stop", log);
        }

        [Fact]
        public async Task SharedMonitorUnknown_PrepareIsFalse_GrabIsNullAndSaysSo()
        {
            // The stream's origin matches no X11 monitor (two monitors, so no single-monitor guess).
            portal.StreamRect = new PhysicalRect(500, 500, 1920, 1080);
            using var g = Create(primary, right);
            Assert.False(await Prepare(g));
            Assert.True(g.IsOpen); // the session is fine; this area just is not on a monitor it knows
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                Assert.Null(await Grab(g));
            }
            Assert.Contains("is not on the shared monitor (unknown)", captured.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Grab_AreaPartlyOffTheSharedMonitor_IsNull()
        {
            using var g = Create(primary, right);
            Assert.True(await Prepare(g));
            int grabs = helper.Rows.Count;
            var across = new PhysicalRect(1900, 100, 100, 10);
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                Assert.Null(await Grab(g, 0, across));
            }
            Assert.Contains($"is not on the shared monitor ({primary.Bounds})", captured.ToString(), StringComparison.Ordinal);
            Assert.Equal(grabs, helper.Rows.Count); // the helper was not asked
            Assert.False(await Prepare(g, across));
        }

        [Theory]
        [InlineData(0L, 1, "pts", "0.0 ms after the strip")]
        [InlineData(5_000_000L, 1, "pts", "16.0 ms after the strip")]
        [InlineData(5_000_000L, 0, "receive time", "16.0 ms after the strip")]
        public async Task Grab_LogsTheFramesAgeAndTimeSource(long _notBeforeNs, int _ptsFromMeta, string _source, string _age)
        {
            // FakeTimeProvider counts 100 ns ticks; the fake helper stamps each frame 16 ms after not_before.
            helper.PtsFromMeta = _ptsFromMeta;
            using var g = Create();
            Assert.True(await Prepare(g));
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                Assert.NotNull(await Grab(g, _notBeforeNs / 100));
            }
            string text = captured.ToString();
            Assert.Contains("screencast: frame 1920x1080 seq 3, ", text, StringComparison.Ordinal);
            Assert.Contains(_age, text, StringComparison.Ordinal);
            Assert.Contains($"({_source})", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Grab_Timeout_IsLoggedWithTheLastSequence()
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            helper.GrabResult = Hidercap.Timeout;
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                Assert.Null(await Grab(g));
            }
            Assert.Contains($"screencast: no frame within {PipeWireGrabber.GrabTimeoutMs} ms (last seq 3)", captured.ToString(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(Hidercap.NotStarted)]
        [InlineData(Hidercap.StreamError)]
        [InlineData(Hidercap.AlreadyStarted)]
        public async Task Grab_HelperError_ClosesAndBacksOff(int _code)
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            helper.GrabResult = _code;
            Assert.Null(await Grab(g));
            Assert.False(g.IsOpen);
            helper.GrabResult = Hidercap.Ok;
            Assert.False(await Prepare(g)); // backed off
            time.Advance(PipeWireGrabber.RetryAfterFailure);
            Assert.True(await Prepare(g));
        }

        [Fact]
        public async Task Grab_CancelledWhileTheHelperWaits_Throws_AndKeepsTheSession()
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            using var cts = new CancellationTokenSource();
            using var block = new ManualResetEventSlim();
            helper.Block = block;
            helper.Entered.Reset();
            var grab = g.GrabAfterAsync(0, defaultNeeded, cts.Token);
            Assert.True(helper.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            cts.Cancel();
            block.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grab);
            Assert.True(g.IsOpen);
            helper.Block = null;
            Assert.NotNull(await Grab(g));
        }

        [Fact]
        public async Task AlreadyCancelled_PrepareAndGrabThrow()
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => g.PrepareAsync(defaultNeeded, cts.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => g.GrabAfterAsync(0, defaultNeeded, cts.Token));
            Assert.NotNull(await Grab(g)); // the gate was not taken
        }

        [Fact]
        public async Task CloseIdleAsync_CloseFails_IsLogged_AndTheGrabberGoesOn()
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            portal.OnClose = () => Task.FromException(new InvalidOperationException("close failed (test)"));
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                await g.CloseIdleAsync(g.IdleGeneration);
            }
            Assert.Contains("screencast: idle close failed: InvalidOperationException: close failed (test)", captured.ToString(), StringComparison.Ordinal);
            Assert.False(g.IsOpen);
            Assert.Contains("stop", log);
            portal.OnClose = null;
            Assert.True(await Prepare(g)); // the gate was released
        }

        [Fact]
        public async Task CloseIdleAsync_NothingOpen_DoesNothing()
        {
            using var g = Create();
            await g.CloseIdleAsync(g.IdleGeneration);
            Assert.Empty(log);
        }

        [Fact]
        public async Task Dispose_CloseHangs_GivesUpAfterTheWait()
        {
            var g = new PipeWireGrabber(portal, helper, tokens, () => new[] { primary }, time) { DisposeWait = TimeSpan.FromMilliseconds(50) };
            Assert.True(await Prepare(g));
            var never = new TaskCompletionSource();
            portal.OnClose = () => never.Task;
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                g.Dispose();
            }
            Assert.Contains("screencast: closing the session at exit timed out", captured.ToString(), StringComparison.Ordinal);
            Assert.Contains("portal dispose", log);
            never.SetResult();
        }

        [Fact]
        public async Task Dispose_CloseFails_IsLogged()
        {
            var g = Create();
            Assert.True(await Prepare(g));
            portal.OnClose = () => throw new InvalidOperationException("close failed (test)");
            using var captured = new StringWriter();
            using (Log.CaptureTo(captured))
            {
                g.Dispose();
            }
            Assert.Contains("screencast: closing the session at exit failed: close failed (test)", captured.ToString(), StringComparison.Ordinal);
            Assert.Contains("portal dispose", log);
            Assert.False(g.IsOpen);
        }

        [Fact]
        public async Task Prepare_FirstGrabNeedsABiggerBuffer_AsksAgain_ThenKeepsIt()
        {
            using var g = Create();
            Assert.True(await Prepare(g));
            // Prepare: buffer too small, then again with the grown buffer (both all rows).
            Assert.Equal(2, helper.Rows.Count);
            Assert.All(helper.Rows, _r => Assert.Equal((0, int.MaxValue), _r));
            await Grab(g);
            Assert.Equal(3, helper.Rows.Count); // the buffer is kept: one call
        }

        [Fact]
        public void Rows_ShownHeightZero_UsesScaleOne()
        {
            Assert.Equal((4, 9), PipeWireGrabber.Rows(new PhysicalRect(0, 0, 100, 0), new PhysicalRect(10, 5, 10, 3), 100));
        }

        [Fact]
        public void Rows_ClampedToTheFrame()
        {
            // Above and below the frame: empty ranges at its edges.
            Assert.Equal((0, 0), PipeWireGrabber.Rows(new PhysicalRect(0, 0, 100, 100), new PhysicalRect(0, -50, 10, 10), 100));
            Assert.Equal((100, 100), PipeWireGrabber.Rows(new PhysicalRect(0, 0, 100, 100), new PhysicalRect(0, 500, 10, 10), 100));
        }

        [Fact]
        public void MapStream_NoScreens_IsNull()
        {
            Assert.Null(PipeWireGrabber.MapStream(new PhysicalRect(0, 0, 10, 10), Array.Empty<ScreenInfo>()));
        }
    }
}
