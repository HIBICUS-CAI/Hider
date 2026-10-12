using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Capture
{
    public class StripCoordinatorTests
    {
        private static readonly string[] wholeAreaLog = { "prepare", "set a 0-240", "grab", "restore a" };
        private static readonly string[] threeStripsLog = {
            "prepare",
            "set a 0-8", "grab", "restore a",
            "set a 8-16", "grab", "restore a",
            "set a 16-20", "grab", "restore a",
        };
        private static readonly string[] overlapLog = {
            "prepare",
            "set me 0-8", "grab", "restore me",
            "set me 8-16", "set over 0-8", "grab", "restore me", "restore over",
        };
        private static readonly string[] restoreBoth = { "restore a", "restore b" };
        private static readonly string[] hangingLog = { "prepare", "set a 0-8", "grab" };
        private static readonly string[] prepareOnly = { "prepare" };

        private static readonly PhysicalRect screenRect = new(0, 0, 640, 480);

        private sealed class Setup : IDisposable
        {
            public readonly List<string> Log = new();
            public readonly FakeTimeProvider Time = new();
            public readonly FakeScreenGrabber Grabber;
            public readonly FakeStripHost Host;
            public readonly List<StripArea> Areas = new();
            public readonly StripCoordinator Coordinator;

            public Setup(FrameBuffer? _frame = null)
            {
                Grabber = new FakeScreenGrabber(Log, _frame ?? CoordinateFrame.Create(screenRect.Width, screenRect.Height));
                Host = new FakeStripHost(Log, Time);
                Coordinator = new StripCoordinator(Grabber, Host, () => Areas.ToList(), Time);
            }

            public StripArea Add(string _id, PhysicalRect _rect, PatternKind _kind = PatternKind.Mosaic)
            {
                var area = new StripArea(_id, _rect, _kind);
                Areas.Add(area);
                return area;
            }

            public void Dispose()
            {
                Coordinator.Dispose(); // and the grabber
            }

            public Task<CaptureResult> Capture(PhysicalRect _rect, int _strip, CancellationToken? _ct = null)
            {
                return Coordinator.CaptureAsync(new CaptureRequest(_rect, Array.Empty<nint>(), _strip), _ct ?? TestContext.Current.CancellationToken);
            }
        }

        [Fact]
        public void CT79_Strips_CountAndShortLast()
        {
            var strips = StripCoordinator.Strips(240, 8).ToList();
            Assert.Equal(30, strips.Count);
            Assert.All(strips, _s => Assert.Equal(8, _s.Y1 - _s.Y0));
            Assert.Equal((232, 240), strips[^1]);

            var odd = StripCoordinator.Strips(250, 8).ToList();
            Assert.Equal(32, odd.Count);
            Assert.Equal((248, 250), odd[^1]);
            Assert.Equal(250, odd.Sum(_s => _s.Y1 - _s.Y0));
        }

        [Fact]
        public async Task CT80_StripZero_OneGrabForTheWholeArea()
        {
            Assert.Equal(new[] { (0, 240) }, StripCoordinator.Strips(240, 0));
            var s = new Setup();
            var rect = new PhysicalRect(10, 20, 100, 240);
            s.Add("a", rect);
            var result = await s.Capture(rect, 0);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal(1, s.Grabber.Calls);
            Assert.Equal(wholeAreaLog, s.Log);
        }

        [Fact]
        public async Task CT81_EachStrip_SetGrabRestore()
        {
            var s = new Setup();
            var rect = new PhysicalRect(10, 20, 100, 20);
            s.Add("a", rect);
            await s.Capture(rect, 8);
            // Each grab asks for its strip's rows only.
            Assert.Equal(new[] { new PhysicalRect(10, 20, 100, 8), new PhysicalRect(10, 28, 100, 8), new PhysicalRect(10, 36, 100, 4) }, s.Grabber.Needed);
            Assert.Equal(threeStripsLog, s.Log);
        }

        [Fact]
        public async Task CT82_GrabWaitsForTheTransparentRows()
        {
            var s = new Setup();
            var rect = new PhysicalRect(10, 20, 100, 24);
            s.Add("a", rect);
            s.Add("b", new PhysicalRect(50, 30, 100, 100));
            await s.Capture(rect, 8);
            Assert.Equal(3, s.Grabber.NotBefore.Count);
            for (int i = 0; i < s.Grabber.NotBefore.Count; i++)
            {
                // Every grab asks for a frame after the last window of its strip finished drawing.
                int grabIndex = s.Log.Select((_e, _k) => (Entry: _e, Index: _k)).Where(_x => _x.Entry == "grab").ElementAt(i).Index;
                int sets = s.Log.Take(grabIndex).Count(_e => _e.StartsWith("set"));
                Assert.True(s.Grabber.NotBefore[i] >= s.Host.SetCompletedAt[sets - 1]);
            }
        }

        [Fact]
        public async Task CT83_OverlappingMosaicAreas_TransparentTogether_OthersUntouched()
        {
            var s = new Setup();
            var rect = new PhysicalRect(100, 100, 200, 16);
            s.Add("me", rect);
            s.Add("over", new PhysicalRect(250, 108, 100, 100));          // overlaps from row 8 of "me"
            s.Add("fill", new PhysicalRect(100, 100, 50, 50), PatternKind.Fill);
            s.Add("gen", new PhysicalRect(120, 100, 50, 50), PatternKind.Generated);
            s.Add("away", new PhysicalRect(400, 300, 50, 50));            // mosaic, not overlapping
            await s.Capture(rect, 8);
            Assert.Equal(overlapLog, s.Log);
        }

        [Fact]
        public async Task CT84_ResultRows_ComeFromTheFrame()
        {
            var s = new Setup();
            var rect = new PhysicalRect(37, 51, 90, 30);
            s.Add("a", rect);
            var result = await s.Capture(rect, 8);
            var frame = result.Frame!;
            Assert.Equal((90, 30), (frame.Width, frame.Height));
            for (int y = 0; y < 30; y++)
            {
                for (int x = 0; x < 90; x++)
                {
                    Assert.Equal((37 + x, 51 + y), CoordinateFrame.Decode(frame, x, y));
                }
            }
        }

        [Fact]
        public async Task CT85_GrabberFails_UnavailableAndRestored()
        {
            var s = new Setup();
            var rect = new PhysicalRect(10, 20, 100, 24);
            s.Add("a", rect);
            s.Add("b", new PhysicalRect(10, 20, 50, 50));
            s.Grabber.FailOnCall = 2;
            var result = await s.Capture(rect, 8);
            Assert.Equal(CaptureFailure.Unavailable, result.Failure);
            Assert.Null(result.Frame);
            Assert.Equal(2, s.Grabber.Calls);
            Assert.Empty(s.Host.Transparent);
            Assert.Equal(restoreBoth, s.Log.TakeLast(2));

            var denied = new Setup();
            denied.Add("a", rect);
            denied.Grabber.Deny = true;
            Assert.Equal(CaptureFailure.Denied, (await denied.Capture(rect, 8)).Failure);
            Assert.Empty(denied.Host.Transparent);

            var noFrame = new Setup();
            noFrame.Add("a", rect);
            noFrame.Grabber.Frame = null;
            Assert.Equal(CaptureFailure.Unavailable, (await noFrame.Capture(rect, 8)).Failure);
            Assert.Empty(noFrame.Host.Transparent);
        }

        [Fact]
        public async Task CT86_Cancelled_RestoredAndNoResult()
        {
            var s = new Setup();
            var rect = new PhysicalRect(10, 20, 100, 24);
            s.Add("a", rect);
            s.Grabber.Hang = true;
            using var cancel = new CancellationTokenSource();
            var task = s.Capture(rect, 8, cancel.Token);
            Assert.Equal(hangingLog, s.Log);
            Assert.Contains("a", s.Host.Transparent);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Empty(s.Host.Transparent);
            Assert.Equal("restore a", s.Log[^1]);
        }

        [Fact]
        public async Task CT87_TwoAreas_GrabsNeverOverlap()
        {
            var s = new Setup();
            var a = s.Add("a", new PhysicalRect(0, 0, 100, 24)).Rect;
            var b = s.Add("b", new PhysicalRect(300, 300, 100, 24)).Rect;
            s.Grabber.Delay = TimeSpan.FromMilliseconds(5);
            var first = Task.Run(() => s.Capture(a, 8));
            var second = Task.Run(() => s.Capture(b, 8));
            var results = await Task.WhenAll(first, second);
            Assert.All(results, _r => Assert.Equal(CaptureFailure.None, _r.Failure));
            Assert.Equal(6, s.Grabber.Calls);
            Assert.Equal(1, s.Grabber.MaxConcurrent);
            // Whole captures are serial too: a's strips are not interleaved with b's.
            var order = s.Log.Where(_e => _e.StartsWith("set")).Select(_e => _e.Split(' ')[1]).ToList();
            Assert.Equal(2, s.Log.Count(_e => _e == "prepare"));
            Assert.True(order.Take(3).Distinct().Count() == 1 && order.Skip(3).Distinct().Count() == 1, string.Join(",", order));
        }

        [Fact]
        public async Task FrameNotCoveringTheArea_Unavailable()
        {
            // A one-monitor grabber (ScreenCast) is asked for the area's rectangle and must cover it.
            var s = new Setup();
            var rect = new PhysicalRect(500, 20, 100, 16);
            s.Add("a", rect);
            s.Grabber.Covers = new PhysicalRect(0, 0, 550, 480);
            Assert.Equal(CaptureFailure.Unavailable, (await s.Capture(rect, 8)).Failure);
            Assert.Equal(new PhysicalRect(500, 20, 100, 8), s.Grabber.Needed[0]); // the first strip's rows
            Assert.Empty(s.Host.Transparent);

            s.Grabber.Frame = CoordinateFrame.Create(2560, 1080);
            s.Grabber.Covers = new PhysicalRect(-1920, 0, 2560, 1080);
            var result = await s.Capture(rect, 8);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal((500 + 1920, 20), CoordinateFrame.Decode(result.Frame!, 0, 0));
        }

        [Fact]
        public async Task AreaAcrossStackedMonitors_StripsComeFromTheMonitorTheyAreOn()
        {
            // ScreenCast shares the top monitor (0..1080) and is asked first; the area spans the border to the bottom one.
            // Its strips on the top monitor come from ScreenCast, the rest from the screenshot of both monitors.
            var log = new List<string>();
            var time = new FakeTimeProvider();
            var screenCast = new FakeScreenGrabber(log, CoordinateFrame.Create(1920, 1080)) { Ready = false, OnlyWhatItCovers = true };
            var screenshot = new FakeScreenGrabber(log, CoordinateFrame.Create(1920, 2160));
            var area = new StripArea("a", new PhysicalRect(100, 1064, 100, 24), PatternKind.Mosaic);
            using var coordinator = new StripCoordinator(new FallbackGrabber(screenCast, screenshot), new FakeStripHost(log, time), () => new[] { area }, time);
            var result = await coordinator.CaptureAsync(new CaptureRequest(area.Rect, Array.Empty<nint>(), 8), TestContext.Current.CancellationToken);
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal((100, 1064), CoordinateFrame.Decode(result.Frame!, 0, 0)); // top monitor, from ScreenCast
            Assert.Equal((100, 1080), CoordinateFrame.Decode(result.Frame!, 0, 16)); // bottom monitor, from the screenshot
            Assert.Equal(3, screenCast.Calls); // asked for every strip, answered the two on its monitor
            Assert.Equal(1, screenshot.Calls);
        }

        [Fact]
        public async Task Prepare_BeforeAnyStrip_FailureExposesNothing()
        {
            // Slow or interactive grabber work (portal session, permission dialog) happens before a strip turns transparent.
            var rect = new PhysicalRect(10, 20, 100, 16);
            var notReady = new Setup();
            notReady.Add("a", rect);
            notReady.Grabber.Ready = false;
            Assert.Equal(CaptureFailure.Unavailable, (await notReady.Capture(rect, 8)).Failure);
            Assert.Equal(prepareOnly, notReady.Log);

            var denied = new Setup();
            denied.Add("a", rect);
            denied.Grabber.DenyPrepare = true;
            Assert.Equal(CaptureFailure.Denied, (await denied.Capture(rect, 8)).Failure);
            Assert.Equal(prepareOnly, denied.Log);
        }

        [Theory]
        [InlineData(1.0, 0, 0)]
        [InlineData(2.0, 0, 0)]
        [InlineData(1.0, -1920, 0)]
        [InlineData(2.0, -1920, -200)]
        [InlineData(1.5, 0, 0)]
        public void CT88_AreaRect_MapsToFrameCoordinates(double _scale, int _originX, int _originY)
        {
            // The frame covers the virtual screen (origin can be negative with a monitor left of the primary) at a scale.
            var bounds = new PhysicalRect(_originX, _originY, 400, 300);
            var frame = CoordinateFrame.Create((int)(bounds.Width * _scale), (int)(bounds.Height * _scale));
            var rect = new PhysicalRect(_originX + 33, _originY + 41, 50, 20);
            var dst = FrameBuffer.Allocate(rect.Width, rect.Height);
            StripCoordinator.CopyRows(frame, bounds, rect, 0, rect.Height, dst);
            for (int y = 0; y < rect.Height; y++)
            {
                for (int x = 0; x < rect.Width; x++)
                {
                    var (fx, fy) = CoordinateFrame.Decode(dst, x, y);
                    Assert.Equal((int)Math.Floor((33 + x + 0.5) * _scale), fx);
                    Assert.Equal((int)Math.Floor((41 + y + 0.5) * _scale), fy);
                }
            }
        }
    }
}
