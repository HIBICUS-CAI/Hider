using Hider.Core.Capture;
using Hider.Core.Model;

namespace Hider.Core.Tests.Capture
{
    public class CaptureTypesTests
    {
        [Fact]
        public void CT99_CaptureFailure_Values()
        {
            Assert.Equal(new[] { CaptureFailure.None, CaptureFailure.Black, CaptureFailure.Denied, CaptureFailure.Unavailable, CaptureFailure.Timeout },
                Enum.GetValues<CaptureFailure>());
            Assert.Equal(0, (int)CaptureFailure.None);
        }

        [Fact]
        public void CT99_CaptureTraits_Flags()
        {
            Assert.Equal(0, (int)CaptureTraits.None);
            Assert.Equal(1, (int)CaptureTraits.Live);
            Assert.Equal(2, (int)CaptureTraits.ExposureFree);
            Assert.Equal(4, (int)CaptureTraits.NeedsStripCooperation);
            var windows = CaptureTraits.Live | CaptureTraits.ExposureFree;
            Assert.True(windows.HasFlag(CaptureTraits.Live));
            Assert.False(windows.HasFlag(CaptureTraits.NeedsStripCooperation));
        }

        [Fact]
        public void CT100_FrameBuffer_Defaults()
        {
            var frame = new FrameBuffer();
            Assert.Equal((0, 0, 0), (frame.Width, frame.Height, frame.Stride));
            Assert.True(frame.Bgra.IsEmpty);
        }

        [Theory]
        [InlineData(3, 2)]
        [InlineData(1, 1)]
        [InlineData(0, 0)]
        [InlineData(0, 5)]
        [InlineData(1920, 1080)]
        public void CT100_Allocate_TightRowsOfZeros(int _w, int _h)
        {
            var frame = FrameBuffer.Allocate(_w, _h);
            Assert.Equal((_w, _h, _w * 4), (frame.Width, frame.Height, frame.Stride));
            Assert.Equal(_w * _h * 4, frame.Bgra.Length);
            Assert.True(frame.Bgra.Span.IndexOfAnyExcept((byte)0) < 0);
        }

        [Theory]
        [InlineData(int.MaxValue, 1)]  // the stride overflows
        [InlineData(50000, 50000)]     // the size overflows
        public void CT100_Allocate_ImpossibleSize_Throws(int _w, int _h)
        {
            Assert.Throws<OverflowException>(() => FrameBuffer.Allocate(_w, _h));
        }

        [Theory]
        [InlineData(-1, 1, "_width")]
        [InlineData(1, -1, "_height")]
        [InlineData(-1, -1, "_width")]
        [InlineData(int.MinValue, 0, "_width")]
        [InlineData(0, int.MinValue, "_height")]
        public void CT100_Allocate_NegativeSize_Throws(int _w, int _h, string _param)
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => FrameBuffer.Allocate(_w, _h));
            Assert.Equal(_param, e.ParamName);
        }

        [Fact]
        public void CT100_Resize_Larger_NewMemory()
        {
            var frame = FrameBuffer.Allocate(2, 2);
            var before = frame.Bgra;
            frame.Resize(3, 4);
            Assert.Equal((3, 4, 12), (frame.Width, frame.Height, frame.Stride));
            Assert.Equal(48, frame.Bgra.Length);
            Assert.False(before.Equals(frame.Bgra));
        }

        [Theory]
        [InlineData(4, 4)]   // same size
        [InlineData(2, 3)]   // smaller
        [InlineData(1, 16)]  // same byte count, other shape
        [InlineData(0, 0)]
        public void CT100_Resize_NotLarger_ReusesTheMemory(int _w, int _h)
        {
            var frame = FrameBuffer.Allocate(4, 4);
            frame.Bgra.Span[63] = 7;
            var before = frame.Bgra;
            frame.Resize(_w, _h);
            Assert.True(before.Equals(frame.Bgra));
            Assert.Equal(64, frame.Bgra.Length); // the buffer keeps its length; Width, Height and Stride say what is used
            Assert.Equal((_w, _h, _w * 4), (frame.Width, frame.Height, frame.Stride));
            Assert.Equal(7, frame.Bgra.Span[63]);
        }

        [Fact]
        public void CT100_Resize_FromEmpty_Allocates()
        {
            var frame = new FrameBuffer();
            frame.Resize(5, 1);
            Assert.Equal(20, frame.Bgra.Length);
            Assert.Equal(20, frame.Stride);
        }

        [Theory]
        [InlineData(50000, 50000)]      // the size overflows
        [InlineData(int.MaxValue, 0)]   // no pixels, but the stride overflows (it would be -4)
        [InlineData(int.MaxValue, 1)]
        [InlineData(0x2000_0000, 0)]    // stride exactly 2^31
        public void CT100_Resize_Overflow_ThrowsAndKeepsTheFrame(int _w, int _h)
        {
            var frame = FrameBuffer.Allocate(2, 2);
            var before = frame.Bgra;
            Assert.Throws<OverflowException>(() => frame.Resize(_w, _h));
            Assert.Equal((2, 2, 8, 16), (frame.Width, frame.Height, frame.Stride, frame.Bgra.Length));
            Assert.True(before.Equals(frame.Bgra));
        }

        [Theory]
        [InlineData(-1, 1, "_width")]   // unchecked it would be a stride of -4 with "needed" below the length
        [InlineData(1, -1, "_height")]
        [InlineData(-1, -1, "_width")]  // the product is positive
        [InlineData(-1, 0, "_width")]
        [InlineData(0, -1, "_height")]
        [InlineData(int.MinValue, 0, "_width")]
        public void CT100_Resize_NegativeSize_ThrowsAndKeepsTheFrame(int _w, int _h, string _param)
        {
            var frame = FrameBuffer.Allocate(2, 2);
            frame.Bgra.Span[5] = 9;
            var before = frame.Bgra;
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => frame.Resize(_w, _h));
            Assert.Equal(_param, e.ParamName);
            Assert.Equal((2, 2, 8, 16), (frame.Width, frame.Height, frame.Stride, frame.Bgra.Length));
            Assert.True(before.Equals(frame.Bgra));
            Assert.Equal(9, frame.Bgra.Span[5]);
        }

        [Fact]
        public void CT100_Resize_LargestStride_NoPixels()
        {
            // A stride just below 2^31 is representable: 0 rows need no memory.
            var frame = FrameBuffer.Allocate(2, 2);
            frame.Resize(0x1FFF_FFFF, 0);
            Assert.Equal((0x1FFF_FFFF, 0, 0x7FFF_FFFC, 16), (frame.Width, frame.Height, frame.Stride, frame.Bgra.Length));
        }

        [Fact]
        public void CT99_CaptureRequest_RecordEquality()
        {
            var windows = new nint[] { 1, 2 };
            var a = new CaptureRequest(new PhysicalRect(1, 2, 3, 4), windows, 8);
            var b = new CaptureRequest(new PhysicalRect(1, 2, 3, 4), windows, 8);
            Assert.Equal(a, b);
            Assert.NotEqual(a, b with { StripPx = 0 });
            Assert.NotEqual(a, b with { ExcludeWindows = new nint[] { 1, 2 } }); // the list is compared by reference
            Assert.Equal(new PhysicalRect(1, 2, 3, 4), a.Rect);
            Assert.Same(windows, a.ExcludeWindows);
            Assert.Equal(8, a.StripPx);
        }

        [Theory]
        [InlineData(CaptureFailure.Black)]
        [InlineData(CaptureFailure.Denied)]
        [InlineData(CaptureFailure.Unavailable)]
        [InlineData(CaptureFailure.Timeout)]
        [InlineData(CaptureFailure.None)]
        public void CT99_CaptureResult_Failed_HasNoFrame(CaptureFailure _failure)
        {
            var result = CaptureResult.Failed(_failure);
            Assert.Null(result.Frame);
            Assert.Equal(_failure, result.Failure);
            Assert.Equal(new CaptureResult(null, _failure), result);
        }

        [Fact]
        public void CT99_CaptureResult_WithFrame()
        {
            var frame = FrameBuffer.Allocate(1, 1);
            var result = new CaptureResult(frame, CaptureFailure.None);
            Assert.Same(frame, result.Frame);
            Assert.Equal(CaptureFailure.None, result.Failure);
        }

        [Fact]
        public async Task CT101_UnavailableCapture_AlwaysUnavailable_ExposureFree()
        {
            using var capture = new UnavailableCapture();
            Assert.Equal(CaptureTraits.ExposureFree, capture.Traits);
            var task = capture.CaptureAsync(new CaptureRequest(new PhysicalRect(0, 0, 10, 10), Array.Empty<nint>(), 8), TestContext.Current.CancellationToken);
            Assert.True(task.IsCompletedSuccessfully); // never waits
            var result = await task;
            Assert.Null(result.Frame);
            Assert.Equal(CaptureFailure.Unavailable, result.Failure);
        }

        [Fact]
        public async Task CT101_UnavailableCapture_IgnoresCancellationAndEmptyRects_DisposeTwice()
        {
            var capture = new UnavailableCapture();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var result = await capture.CaptureAsync(new CaptureRequest(default, Array.Empty<nint>(), 0), cancelled.Token);
            Assert.Equal(CaptureFailure.Unavailable, result.Failure);
            capture.Dispose();
            capture.Dispose();
            Assert.Equal(CaptureTraits.ExposureFree, ((IUnderlayCapture)capture).Traits);
        }
    }
}
