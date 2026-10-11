using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Capture
{
    public class UnderlayComposerTests
    {
        private const nint Area = 100, OtherArea = 101, Progman = 999;
        private static readonly PhysicalRect Screen = new(0, 0, 1920, 1080);
        private static readonly (byte, byte, byte) Red = (200, 0, 0), Green = (0, 200, 0), Blue = (0, 0, 200), Desktop = (30, 60, 90);

        private static CaptureRequest Request(PhysicalRect _rect, params nint[] _exclude)
        {
            return new CaptureRequest(_rect, _exclude.Length > 0 ? _exclude : new[] { Area }, 8);
        }

        private static (byte R, byte G, byte B) At(FrameBuffer _f, int _x, int _y)
        {
            int i = _y * _f.Stride + _x * 4;
            var px = _f.Bgra.Span;
            return (px[i + 2], px[i + 1], px[i]);
        }

        [Fact]
        public void CT61_ZOrder_UpperWindowCoversLower()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(100, 100, 200, 100), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(150, 100, 100, 100), Red)
                .Add(2, new PhysicalRect(0, 0, 800, 600), Blue)
                .Add(Progman, Screen, Desktop, "Progman");
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(100, 100, 200, 100)));
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal(Blue, At(result.Frame!, 10, 10));
            Assert.Equal(Red, At(result.Frame!, 60, 10));
            Assert.DoesNotContain(Progman, source.Captured); // fully covered by window 2: never captured
        }

        [Fact]
        public void CT62_Clip_PartialIntersectionOffsets()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(250, 120, 400, 300), Green, _border: 7)
                .Add(Progman, Screen, Desktop, "Progman");
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(200, 100, 100, 50)));
            var f = result.Frame!;
            Assert.Equal((100, 50), (f.Width, f.Height));
            Assert.Equal(Desktop, At(f, 49, 19));  // left of the window's frame
            Assert.Equal(Green, At(f, 50, 20));    // window frame starts at (250,120) = area (50,20)
            Assert.Equal(Desktop, At(f, 45, 30));  // the 7 px invisible border is never copied
        }

        [Fact]
        public void CT63_HiderWindows_AreLeftOut()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 400, 400), (9, 9, 9), "Hider")
                .Add(OtherArea, new PhysicalRect(0, 0, 400, 400), (7, 7, 7), "Hider")
                .Add(1, new PhysicalRect(0, 0, 400, 400), Red);
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(50, 50, 100, 100), Area, OtherArea));
            Assert.Equal(Red, At(result.Frame!, 5, 5));
            Assert.DoesNotContain(OtherArea, source.Captured);
            Assert.DoesNotContain(Area, source.Captured);
        }

        [Fact]
        public void CT64_Filters_InvisibleCloakedEmptyAndDisjoint()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(0, 0, 500, 500), Red, _visible: false)
                .Add(2, new PhysicalRect(0, 0, 500, 500), Green, _cloaked: true)
                .Add(3, new PhysicalRect(10, 10, 0, 0), Blue)
                .Add(4, new PhysicalRect(1000, 1000, 50, 50), Blue)
                .Add(Progman, Screen, Desktop, "Progman");
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(20, 20, 60, 60)));
            Assert.Equal(new nint[] { Progman }, source.Captured);
            Assert.Equal(Desktop, At(result.Frame!, 30, 30));
        }

        [Fact]
        public void CT65_WindowsAboveTheArea_AreNotCaptured()
        {
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 500, 500), Red, "AlwaysOnTopApp")
                .Add(Area, new PhysicalRect(0, 0, 100, 100), (9, 9, 9), "Hider")
                .Add(2, new PhysicalRect(0, 0, 500, 500), Green);
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100)));
            Assert.Equal(Green, At(result.Frame!, 50, 50));
            Assert.DoesNotContain((nint)1, source.Captured);
        }

        [Fact]
        public void CT66_EmptyRegion_ShowsProgman()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(0, 0, 50, 50), Red)
                .Add(Progman, Screen, Desktop, "Progman");
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100)));
            Assert.Equal(Red, At(result.Frame!, 10, 10));
            Assert.Equal(Desktop, At(result.Frame!, 80, 80));
        }

        [Fact]
        public void CT67_AllBlack_IsBlack()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, Screen, Red, _black: true);
            Assert.Equal(CaptureFailure.Black, new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100))).Failure);
        }

        [Fact]
        public void CT68_OneBlackWindow_IsBlack()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(0, 0, 50, 100), Red)
                .Add(2, new PhysicalRect(50, 0, 50, 100), Red, _black: true)
                .Add(Progman, Screen, Desktop, "Progman");
            Assert.Equal(CaptureFailure.Black, new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100))).Failure);
        }

        [Fact]
        public void CT69_Timeout_FailsAndIsSkippedNextTime()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(0, 0, 50, 100), Red, _failure: CaptureFailure.Timeout)
                .Add(Progman, Screen, Desktop, "Progman");
            var composer = new UnderlayComposer(source, TimeSpan.FromMilliseconds(50));
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100))).Failure);
            source.Captured.Clear();
            var next = composer.Compose(Request(new PhysicalRect(0, 0, 100, 100)));
            Assert.Equal(CaptureFailure.None, next.Failure);
            Assert.DoesNotContain((nint)1, source.Captured);
            source.Captured.Clear();
            composer.Compose(Request(new PhysicalRect(0, 0, 100, 100)));
            Assert.Contains((nint)1, source.Captured); // skipped once only
        }

        [Fact]
        public void CT69_TimeoutSkip_BelongsToTheAreaThatTimedOut()
        {
            // Area A times out on window 1; area B (another own window, also above window 1) must still capture it.
            const nint areaB = 77;
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(areaB, new PhysicalRect(200, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(0, 0, 50, 100), Red, _failure: CaptureFailure.Timeout)
                .Add(Progman, Screen, Desktop, "Progman");
            var composer = new UnderlayComposer(source, TimeSpan.FromMilliseconds(50));
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area, areaB)).Failure);
            source.Captured.Clear();
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), areaB, Area)).Failure);
            Assert.Contains((nint)1, source.Captured);
            source.Captured.Clear();
            Assert.Equal(CaptureFailure.None, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area, areaB)).Failure);
            Assert.DoesNotContain((nint)1, source.Captured);
        }

        [Fact]
        public void CT70_FrameBounds_OffsetWithinPrintWindowBitmap()
        {
            // PrintWindow draws GetWindowRect, which is larger than the DWM frame by the invisible borders.
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(108, 100, 300, 200), Blue, _border: 8)
                .Add(Progman, Screen, Desktop, "Progman");
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(100, 100, 100, 100)));
            var f = result.Frame!;
            Assert.Equal(Desktop, At(f, 7, 50));   // x = 107: border pixel (1,2,3 marker) not used
            Assert.Equal(Blue, At(f, 8, 50));      // x = 108: first frame column
            Assert.Equal(Blue, At(f, 8, 0));
        }
    
        [Fact]
        public void PixelsNoWindowCovers_AreOpaqueBlack_AlsoWhenTheBufferIsReused()
        {
            // A gap between monitors: no window there. The frame is drawn with BlendMode.Src, so it must stay opaque.
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(100, 100, 50, 100), Red);
            var composer = new UnderlayComposer(source);
            var big = composer.Compose(Request(new PhysicalRect(100, 100, 100, 100)));
            Assert.Equal(CaptureFailure.None, big.Failure);
            Assert.Equal(Red, At(big.Frame!, 10, 10));
            int gap = 10 * big.Frame!.Stride + 70 * 4;
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, big.Frame.Bgra.Span.Slice(gap, 4).ToArray());

            // A smaller area after a bigger one reuses the coverage buffer: nothing of the first call leaks in.
            var small = composer.Compose(Request(new PhysicalRect(140, 100, 40, 40)));
            Assert.Equal(Red, At(small.Frame!, 5, 5));
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, small.Frame!.Bgra.Span.Slice(5 * small.Frame.Stride + 20 * 4, 4).ToArray());
        }
    }
}
