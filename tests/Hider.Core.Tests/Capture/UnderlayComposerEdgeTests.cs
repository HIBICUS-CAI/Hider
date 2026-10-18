using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;

namespace Hider.Core.Tests.Capture
{
    /// <summary>The branches of <see cref="UnderlayComposer"/> that the CT-61 .. CT-70 tests do not reach.</summary>
    public class UnderlayComposerEdgeTests
    {
        private const nint Area = 100, Progman = 999;
        private static readonly PhysicalRect screen = new(0, 0, 1920, 1080);
        private static readonly (byte, byte, byte) red = (200, 0, 0), green = (0, 200, 0), blue = (0, 0, 200), desktop = (30, 60, 90);
        private static readonly nint[] onlyWindowOne = { 1 };
        private static readonly nint[] onlyWindowTwo = { 2 };
        private static readonly nint[] onlyWindowThree = { 3 };
        private static readonly nint[] oneThenTwo = { 1, 2 };
        private static readonly nint[] oneThenDesktop = { 1, Progman };
        private static readonly nint[] twoThenDesktop = { 2, Progman };

        /// <summary>Windows with images given by a function (any size, null, any failure).</summary>
        private sealed class ScriptedSource : IWindowSource
        {
            public List<WindowInfo> Windows { get; } = new();
            public Func<WindowInfo, WindowImage> Image { get; set; } = _ => new WindowImage(null, CaptureFailure.None);
            public int Enumerations { get; private set; }
            public List<TimeSpan> Timeouts { get; } = new();

            public IReadOnlyList<WindowInfo> EnumerateTopDown()
            {
                Enumerations++;
                return Windows;
            }

            public WindowImage Capture(WindowInfo _window, TimeSpan _timeout)
            {
                Timeouts.Add(_timeout);
                return Image(_window);
            }
        }

        private static CaptureRequest Request(PhysicalRect _rect, params nint[] _exclude)
        {
            return new CaptureRequest(_rect, _exclude, 8);
        }

        private static (byte R, byte G, byte B, byte A) At(FrameBuffer _f, int _x, int _y)
        {
            int i = _y * _f.Stride + _x * 4;
            var px = _f.Bgra.Span;
            return (px[i + 2], px[i + 1], px[i], px[i + 3]);
        }

        private static FrameBuffer Solid(int _w, int _h, (byte R, byte G, byte B) _color, byte _alpha = 255)
        {
            var image = FrameBuffer.Allocate(_w, _h);
            var px = image.Bgra.Span;
            for (int i = 0; i < px.Length; i += 4)
            {
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = (_color.B, _color.G, _color.R, _alpha);
            }
            return image;
        }

        private static WindowInfo Window(nint _handle, PhysicalRect _rect, bool _visible = true, bool _cloaked = false)
        {
            return new WindowInfo(_handle, _rect, _rect, _visible, _cloaked, "App");
        }

        [Fact]
        public void CT69_Timeouts_DefaultAndCustom()
        {
            Assert.Equal(TimeSpan.FromMilliseconds(250), UnderlayComposer.DefaultTimeout);
            Assert.Equal(UnderlayComposer.DefaultTimeout, new UnderlayComposer(new FakeWindowSource()).Timeout);
            Assert.Equal(TimeSpan.FromSeconds(3), new UnderlayComposer(new FakeWindowSource(), TimeSpan.FromSeconds(3)).Timeout);
        }

        [Fact]
        public void CT69_Capture_GetsTheComposersTimeout()
        {
            var source = new ScriptedSource { Image = _w => new WindowImage(Solid(_w.WindowRect.Width, _w.WindowRect.Height, red), CaptureFailure.None) };
            source.Windows.Add(Window(1, new PhysicalRect(0, 0, 10, 10)));
            new UnderlayComposer(source, TimeSpan.FromMilliseconds(77)).Compose(Request(new PhysicalRect(0, 0, 10, 10)));
            Assert.Equal(TimeSpan.FromMilliseconds(77), Assert.Single(source.Timeouts));
        }

        [Fact]
        public void CT106_LastCaptured_EmptyAtFirst_ThenTheWindowsOfTheLastCall()
        {
            var source = new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(0, 0, 50, 100), red)
                .Add(2, new PhysicalRect(0, 0, 100, 100), green);
            var composer = new UnderlayComposer(source);
            Assert.Empty(composer.LastCaptured);
            composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(oneThenTwo, composer.LastCaptured);
            composer.Compose(Request(new PhysicalRect(60, 0, 40, 40), Area));
            Assert.Equal(onlyWindowTwo, composer.LastCaptured);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(10, 0)]
        [InlineData(-1, -1)]
        public void CT62_EmptyRect_UnavailableWithoutEnumerating(int _w, int _h)
        {
            var source = new ScriptedSource();
            var composer = new UnderlayComposer(source);
            var result = composer.Compose(Request(new PhysicalRect(5, 5, _w, _h), Area));
            Assert.Equal(CaptureFailure.Unavailable, result.Failure);
            Assert.Null(result.Frame);
            Assert.Equal(0, source.Enumerations);
            Assert.Empty(composer.LastCaptured);
        }

        [Fact]
        public void CT63_NoExcludedWindows_EveryWindowFromTheTopCounts()
        {
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 50, 100), red)
                .Add(2, new PhysicalRect(0, 0, 100, 100), green);
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100)));
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal((200, 0, 0, 255), At(result.Frame!, 10, 10));
            Assert.Equal((0, 200, 0, 255), At(result.Frame!, 80, 10));
        }

        [Fact]
        public void CT69_NoExcludedWindows_TimeoutSkipStillWorks()
        {
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 50, 100), red, _failure: CaptureFailure.Timeout)
                .Add(Progman, screen, desktop, "Progman");
            var composer = new UnderlayComposer(source);
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100))).Failure);
            Assert.Equal(CaptureFailure.None, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100))).Failure);
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100))).Failure);
        }

        [Fact]
        public void CT65_OwnWindowNotListed_StartsAtTheTop()
        {
            // The area's window is not (yet) in the Z order: nothing is known to be above it, so every window counts.
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 100, 100), red)
                .Add(2, new PhysicalRect(0, 0, 100, 100), green);
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal((200, 0, 0, 255), At(result.Frame!, 50, 50));
            Assert.DoesNotContain((nint)2, source.Captured); // covered by window 1
        }

        [Fact]
        public void CT66_OwnWindowLast_NothingBelow_OpaqueBlack()
        {
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 100, 100), red)
                .Add(Area, new PhysicalRect(0, 0, 100, 100), (9, 9, 9), "Hider");
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal((0, 0, 0, 255), At(result.Frame!, 50, 50));
            Assert.Empty(source.Captured);
        }

        [Theory]
        [InlineData(CaptureFailure.Denied)]
        [InlineData(CaptureFailure.Unavailable)]
        [InlineData(CaptureFailure.Black)]
        public void CT69_OtherFailures_FailTheCapture_NotSkippedNextTime(CaptureFailure _failure)
        {
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 50, 100), red, _failure: _failure)
                .Add(2, new PhysicalRect(0, 0, 100, 100), green);
            var composer = new UnderlayComposer(source);
            var result = composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(_failure, result.Failure);
            Assert.Null(result.Frame);
            Assert.DoesNotContain((nint)2, source.Captured); // stopped at the failing window
            source.Captured.Clear();
            Assert.Equal(_failure, composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area)).Failure);
            Assert.Contains((nint)1, source.Captured);
        }

        [Fact]
        public void CT67_NoImageWithoutAFailure_IsBlack()
        {
            var source = new ScriptedSource();
            source.Windows.Add(Window(1, new PhysicalRect(0, 0, 10, 10)));
            Assert.Equal(CaptureFailure.Black, new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 10, 10))).Failure);
        }

        [Fact]
        public void CT69_TimeoutStopsTheWalk_TheTimedOutWindowIsInLastCaptured()
        {
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 50, 100), red, _failure: CaptureFailure.Timeout)
                .Add(2, new PhysicalRect(50, 0, 50, 100), green);
            var composer = new UnderlayComposer(source);
            composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(onlyWindowOne, composer.LastCaptured);
        }

        [Fact]
        public void CT69_SkippedWindow_ItsPartComesFromBelow()
        {
            // Window 1 timed out last time: this time it is skipped and the window below shows through.
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 50, 100), red, _failure: CaptureFailure.Timeout)
                .Add(2, new PhysicalRect(0, 0, 100, 100), green);
            var composer = new UnderlayComposer(source);
            composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            var result = composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal((0, 200, 0, 255), At(result.Frame!, 10, 10));
            Assert.Equal(onlyWindowTwo, composer.LastCaptured);
        }

        [Fact]
        public void CT61_WindowWhosePartIsAlreadyCovered_IsSkipped_LowerWindowsStillFillTheRest()
        {
            // Window 2 lies inside window 1; window 3 fills the right half. Window 2 is never captured.
            var source = new FakeWindowSource()
                .Add(1, new PhysicalRect(0, 0, 50, 100), red)
                .Add(2, new PhysicalRect(10, 10, 20, 20), blue)
                .Add(3, new PhysicalRect(0, 0, 100, 100), green);
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(oneAndThree, source.Captured);
            Assert.Equal((200, 0, 0, 255), At(result.Frame!, 20, 20));
            Assert.Equal((0, 200, 0, 255), At(result.Frame!, 70, 20));
        }

        private static readonly nint[] oneAndThree = { 1, 3 };

        [Fact]
        public void CT64_EmptyWindowRect_OrDisjointFrame_IsSkipped()
        {
            var source = new ScriptedSource { Image = _w => new WindowImage(Solid(_w.WindowRect.Width, _w.WindowRect.Height, _w.Handle == 3 ? green : red), CaptureFailure.None) };
            source.Windows.Add(new WindowInfo(1, default, new PhysicalRect(0, 0, 100, 100), true, false, "NoWindowRect"));
            source.Windows.Add(new WindowInfo(2, new PhysicalRect(0, 0, 40, 40), new PhysicalRect(60, 60, 40, 40), true, false, "FrameOutsideItsRect"));
            source.Windows.Add(Window(3, new PhysicalRect(0, 0, 100, 100)));
            var composer = new UnderlayComposer(source);
            var result = composer.Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(onlyWindowThree, composer.LastCaptured);
            Assert.Equal((0, 200, 0, 255), At(result.Frame!, 70, 70));
        }

        [Fact]
        public void CT70_ImageSmallerThanTheWindow_MissingPixelsComeFromBelow()
        {
            // PrintWindow returned a 30 x 20 image for a 50 x 50 window: the rest of its part is left to lower windows.
            var source = new ScriptedSource
            {
                Image = _w => _w.Handle == 1
                    ? new WindowImage(Solid(30, 20, red), CaptureFailure.None)
                    : new WindowImage(Solid(_w.WindowRect.Width, _w.WindowRect.Height, green), CaptureFailure.None),
            };
            source.Windows.Add(Window(1, new PhysicalRect(10, 10, 50, 50)));
            source.Windows.Add(Window(2, new PhysicalRect(0, 0, 100, 100)));
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 100, 100), Area));
            Assert.Equal(CaptureFailure.None, result.Failure);
            Assert.Equal((200, 0, 0, 255), At(result.Frame!, 10, 10));
            Assert.Equal((200, 0, 0, 255), At(result.Frame!, 39, 29));
            Assert.Equal((0, 200, 0, 255), At(result.Frame!, 40, 10)); // x past the image
            Assert.Equal((0, 200, 0, 255), At(result.Frame!, 10, 30)); // y past the image
        }

        [Fact]
        public void CT106_TranslucentPixels_StoredOpaque()
        {
            var source = new ScriptedSource { Image = _w => new WindowImage(Solid(_w.WindowRect.Width, _w.WindowRect.Height, red, 10), CaptureFailure.None) };
            source.Windows.Add(Window(1, new PhysicalRect(0, 0, 10, 10)));
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 10, 10), Area));
            Assert.Equal((200, 0, 0, 255), At(result.Frame!, 5, 5));
        }

        [Fact]
        public void CT106_Target_IsReusedAndResized()
        {
            var source = new FakeWindowSource().Add(1, new PhysicalRect(0, 0, 100, 100), red);
            var target = FrameBuffer.Allocate(200, 200);
            var memory = target.Bgra;
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 30, 20), Area), target);
            Assert.Same(target, result.Frame);
            Assert.Equal((30, 20, 120), (target.Width, target.Height, target.Stride));
            Assert.True(memory.Equals(target.Bgra));
            Assert.Equal((200, 0, 0, 255), At(target, 29, 19));
        }

        [Fact]
        public void CT106_TargetTooSmall_GrowsAndIsStillReturned()
        {
            var source = new FakeWindowSource().Add(1, new PhysicalRect(0, 0, 100, 100), red);
            var target = new FrameBuffer();
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 30, 20), Area), target);
            Assert.Same(target, result.Frame);
            Assert.Equal(30 * 20 * 4, target.Bgra.Length);
        }

        [Fact]
        public void CT106_Failure_LeavesTheTargetButReturnsNoFrame()
        {
            var source = new FakeWindowSource().Add(1, new PhysicalRect(0, 0, 100, 100), red, _black: true);
            var target = FrameBuffer.Allocate(10, 10);
            var result = new UnderlayComposer(source).Compose(Request(new PhysicalRect(0, 0, 10, 10), Area), target);
            Assert.Equal(CaptureFailure.Black, result.Failure);
            Assert.Null(result.Frame);
        }

        [Fact]
        public void CT67_IsBlack_RgbZeroWhateverTheAlpha()
        {
            Assert.True(UnderlayComposer.IsBlack(Solid(4, 3, (0, 0, 0), 255)));
            Assert.True(UnderlayComposer.IsBlack(Solid(4, 3, (0, 0, 0), 0)));
            Assert.True(UnderlayComposer.IsBlack(new FrameBuffer()));
            Assert.True(UnderlayComposer.IsBlack(FrameBuffer.Allocate(0, 7)));
        }

        [Theory]
        [InlineData(0)] // blue
        [InlineData(1)] // green
        [InlineData(2)] // red
        public void CT67_IsBlack_AnyColorChannel_NotBlack(int _channel)
        {
            var image = Solid(4, 3, (0, 0, 0));
            image.Bgra.Span[(2 * 4 + 3) * 4 + _channel] = 1; // the last pixel
            Assert.False(UnderlayComposer.IsBlack(image));
        }

        [Fact]
        public void CT67_IsBlack_IgnoresTheRowPadding()
        {
            // Stride 24 for 4 pixels: the 8 padding bytes per row are not pixels.
            var image = new FrameBuffer { Width = 4, Height = 2, Stride = 24, Bgra = new byte[48] };
            image.Bgra.Span.Slice(16, 8).Fill(0xFF);
            image.Bgra.Span.Slice(40, 8).Fill(0xFF);
            Assert.True(UnderlayComposer.IsBlack(image));
            image.Bgra.Span[24] = 5; // first pixel of row 1
            Assert.False(UnderlayComposer.IsBlack(image));
        }

        /// <summary>Area, window 1 (left half), window 2 (right half) and the desktop.</summary>
        private static FakeWindowSource TwoHalves(CaptureFailure _first = CaptureFailure.None, CaptureFailure _second = CaptureFailure.None)
        {
            return new FakeWindowSource()
                .Add(Area, new PhysicalRect(0, 0, 10, 10), (9, 9, 9), "Hider")
                .Add(1, new PhysicalRect(0, 0, 50, 100), red, _failure: _first)
                .Add(2, new PhysicalRect(50, 0, 50, 100), green, _failure: _second)
                .Add(Progman, screen, desktop, "Progman");
        }

        private static void SetFailure(FakeWindowSource _source, nint _handle, CaptureFailure _failure, bool _black = false)
        {
            int i = _source.TopDown.FindIndex(_e => _e.Info.Handle == _handle);
            _source.TopDown[i] = _source.TopDown[i] with { Failure = _failure, Black = _black };
        }

        [Fact]
        public void CT69_TimeoutSkip_IsUsedUpByTheNextCapture_EvenWhenThatOneFails()
        {
            // The skip is for the next capture of the area, whatever its outcome: when that one fails on another window
            // (black here), the window that timed out is asked again the time after.
            var source = TwoHalves(_first: CaptureFailure.Timeout);
            var composer = new UnderlayComposer(source);
            var rect = new PhysicalRect(0, 0, 100, 100);
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(rect, Area)).Failure);
            SetFailure(source, 1, CaptureFailure.None);
            SetFailure(source, 2, CaptureFailure.None, _black: true);
            source.Captured.Clear();
            Assert.Equal(CaptureFailure.Black, composer.Compose(Request(rect, Area)).Failure);
            Assert.Equal(onlyWindowTwo, source.Captured);
            SetFailure(source, 2, CaptureFailure.None);
            source.Captured.Clear();
            Assert.Equal(CaptureFailure.None, composer.Compose(Request(rect, Area)).Failure);
            Assert.Equal(oneThenTwo, source.Captured);
        }

        [Fact]
        public void CT69_TimeoutSkip_ANewTimeoutReplacesIt()
        {
            // Window 1 times out; the next capture skips it and times out on window 2; the one after asks 1 again and skips 2.
            var source = TwoHalves(_first: CaptureFailure.Timeout);
            var composer = new UnderlayComposer(source);
            var rect = new PhysicalRect(0, 0, 100, 100);
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(rect, Area)).Failure);
            SetFailure(source, 1, CaptureFailure.None);
            SetFailure(source, 2, CaptureFailure.Timeout);
            source.Captured.Clear();
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(rect, Area)).Failure);
            Assert.Equal(onlyWindowTwo, source.Captured);
            SetFailure(source, 2, CaptureFailure.None);
            source.Captured.Clear();
            var third = composer.Compose(Request(rect, Area));
            Assert.Equal(CaptureFailure.None, third.Failure);
            Assert.Equal(oneThenDesktop, source.Captured);
            Assert.Equal(desktop, ((byte, byte, byte))(At(third.Frame!, 75, 50).R, At(third.Frame!, 75, 50).G, At(third.Frame!, 75, 50).B));
        }

        [Fact]
        public void CT69_TimeoutSkip_KeptOverAnEmptyRequest()
        {
            // A request with an empty rectangle fails before anything is captured: it does not use up the skip.
            var source = TwoHalves(_first: CaptureFailure.Timeout);
            var composer = new UnderlayComposer(source);
            var rect = new PhysicalRect(0, 0, 100, 100);
            Assert.Equal(CaptureFailure.Timeout, composer.Compose(Request(rect, Area)).Failure);
            Assert.Equal(CaptureFailure.Unavailable, composer.Compose(Request(new PhysicalRect(0, 0, 0, 10), Area)).Failure);
            SetFailure(source, 1, CaptureFailure.None);
            source.Captured.Clear();
            Assert.Equal(CaptureFailure.None, composer.Compose(Request(rect, Area)).Failure);
            Assert.Equal(twoThenDesktop, source.Captured); // 1 skipped: the desktop shows through its half
        }
    }
}
