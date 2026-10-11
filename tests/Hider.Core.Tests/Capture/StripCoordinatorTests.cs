using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Capture
{
    public class StripCoordinatorTests
    {
        private static readonly PhysicalRect Screen = new(0, 0, 640, 480);

        private sealed class Setup
        {
            public readonly List<string> Log = new();
            public readonly FakeTimeProvider Time = new();
            public readonly FakeScreenGrabber Grabber;
            public readonly FakeStripHost Host;
            public readonly List<StripArea> Areas = new();
            public PhysicalRect Bounds = Screen;
            public readonly StripCoordinator Coordinator;

            public Setup(FrameBuffer? _frame = null)
            {
                Grabber = new FakeScreenGrabber(Log, _frame ?? CoordinateFrame.Create(Screen.Width, Screen.Height));
                Host = new FakeStripHost(Log, Time);
                Coordinator = new StripCoordinator(Grabber, Host, () => Areas.ToList(), () => Bounds, Time);
            }

            public StripArea Add(string _id, PhysicalRect _rect, PatternKind _kind = PatternKind.Mosaic)
            {
                var area = new StripArea(_id, _rect, _kind);
                Areas.Add(area);
                return area;
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
            Assert.All(strips, s => Assert.Equal(8, s.Y1 - s.Y0));
            Assert.Equal((232, 240), strips[^1]);

            var odd = StripCoordinator.Strips(250, 8).ToList();
            Assert.Equal(32, odd.Count);
            Assert.Equal((248, 250), odd[^1]);
            Assert.Equal(250, odd.Sum(s => s.Y1 - s.Y0));
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
            Assert.Equal(new[] { "set a 0-240", "grab", "restore a" }, s.Log);
        }

        [Fact]
        public async Task CT81_EachStrip_SetGrabRestore()
        {
            var s = new Setup();
            var rect = new PhysicalRect(10, 20, 100, 20);
            s.Add("a", rect);
            await s.Capture(rect, 8);
            Assert.Equal(new[]
            {
                "set a 0-8", "grab", "restore a",
                "set a 8-16", "grab", "restore a",
                "set a 16-20", "grab", "restore a",
            }, s.Log);
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
                int grabIndex = s.Log.Select((e, k) => (e, k)).Where(x => x.e == "grab").ElementAt(i).k;
                int sets = s.Log.Take(grabIndex).Count(e => e.StartsWith("set"));
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
            Assert.Equal(new[]
            {
                "set me 0-8", "grab", "restore me",
                "set me 8-16", "set over 0-8", "grab", "restore me", "restore over",
            }, s.Log);
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
            Assert.Equal(new[] { "restore a", "restore b" }, s.Log.TakeLast(2));

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
            Assert.Equal(new[] { "set a 0-8", "grab" }, s.Log);
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
            Assert.All(results, r => Assert.Equal(CaptureFailure.None, r.Failure));
            Assert.Equal(6, s.Grabber.Calls);
            Assert.Equal(1, s.Grabber.MaxConcurrent);
            // Whole captures are serial too: a's strips are not interleaved with b's.
            var order = s.Log.Where(e => e.StartsWith("set")).Select(e => e.Split(' ')[1]).ToList();
            Assert.True(order.Take(3).Distinct().Count() == 1 && order.Skip(3).Distinct().Count() == 1, string.Join(",", order));
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
