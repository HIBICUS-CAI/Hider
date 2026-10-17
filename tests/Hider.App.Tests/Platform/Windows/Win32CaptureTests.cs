using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Platform.Windows;

namespace Hider.App.Tests.Platform.Windows
{
    /// <summary>
    /// Win32WindowSource and Win32UnderlayCapture. What runs before any native call (traits, cancellation, the caches, and
    /// the in-flight / timeout rules around a PrintWindow stand-in) is checked on every OS; enumeration and PrintWindow on
    /// Windows only, on a window the test creates.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class Win32CaptureTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        private static readonly TimeSpan longWait = TimeSpan.FromSeconds(30);

        private static int CachedImages(Win32WindowSource _source) => Count(_source, "images");

        private static int Count(object _owner, string _field)
        {
            return Map(_owner, _field).Count;
        }

        private static IDictionary Map(object _owner, string _field)
        {
            var field = _owner.GetType().GetField(_field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field); // renamed: update these tests
            return (IDictionary)field!.GetValue(_owner)!;
        }

        /// <summary>The PrintWindow call still recorded for <paramref name="_handle"/>.</summary>
        private static Task InFlight(Win32WindowSource _source, nint _handle)
        {
            return (Task)Map(_source, "inFlight")[_handle]!;
        }

        private static WindowInfo Window(nint _handle, int _width, int _height)
        {
            var rect = new PhysicalRect(0, 0, _width, _height);
            return new WindowInfo(_handle, rect, rect, true, false, "test");
        }

        /// <summary>
        /// A PrintWindow stand-in: answers each window with an image of its own, hangs on the windows the test holds until
        /// they are released, or throws while <see cref="Throw"/> is set.
        /// </summary>
        private sealed class Printer : IDisposable
        {
            private readonly Dictionary<nint, ManualResetEventSlim> held = new();
            private readonly Dictionary<nint, int> calls = new();
            private readonly Dictionary<nint, FrameBuffer> images = new();

            public SemaphoreSlim Entered { get; } = new(0);

            public Exception? Throw { get; set; }

            public void Hold(nint _handle)
            {
                lock (held)
                {
                    held[_handle] = new ManualResetEventSlim(false);
                }
            }

            public void Release(nint _handle)
            {
                lock (held)
                {
                    held[_handle].Set();
                }
            }

            public int Calls(nint _handle)
            {
                lock (calls)
                {
                    return calls.GetValueOrDefault(_handle);
                }
            }

            public FrameBuffer Image(nint _handle)
            {
                lock (images)
                {
                    if (!images.TryGetValue(_handle, out var image))
                    {
                        image = FrameBuffer.Allocate(2, 1);
                        images[_handle] = image;
                    }
                    return image;
                }
            }

            public WindowImage Print(WindowInfo _window)
            {
                lock (calls)
                {
                    calls[_window.Handle] = calls.GetValueOrDefault(_window.Handle) + 1;
                }
                Entered.Release();
                ManualResetEventSlim? gate;
                lock (held)
                {
                    held.TryGetValue(_window.Handle, out gate);
                }
                gate?.Wait();
                if (Throw is { } e)
                {
                    throw e;
                }
                return new WindowImage(Image(_window.Handle), CaptureFailure.None);
            }

            public void Dispose()
            {
                lock (held)
                {
                    foreach (var gate in held.Values)
                    {
                        gate.Set();
                    }
                }
            }
        }

        [Fact]
        public void UnderlayCapture_IsLiveAndExposureFree()
        {
            // NFR-01 / NFR-02: read from DWM, nothing is hidden or excluded while capturing.
            using var capture = new Win32UnderlayCapture();

            Assert.Equal(CaptureTraits.Live | CaptureTraits.ExposureFree, capture.Traits);
            Assert.False(capture.Traits.HasFlag(CaptureTraits.NeedsStripCooperation));
        }

        [Fact]
        public void UnderlayCapture_Cancelled_ThrowsBeforeCapturing()
        {
            using var capture = new Win32UnderlayCapture();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            // Thrown at once, under the lock, before any window is enumerated (the method is synchronous).
            Assert.Throws<OperationCanceledException>(() =>
            {
                _ = capture.CaptureAsync(new CaptureRequest(new PhysicalRect(0, 0, 8, 8), Array.Empty<nint>(), 0), cancelled.Token);
            });
            Assert.Equal(0, Count(capture, "outputs"));
        }

        [Fact]
        public void UnderlayCapture_Dispose_IsIdempotent()
        {
            var capture = new Win32UnderlayCapture();

            capture.Dispose();
            capture.Dispose();

            Assert.Equal(CaptureTraits.Live | CaptureTraits.ExposureFree, capture.Traits);
        }

        [Fact]
        public void WindowSource_New_HasEnumeratedNothing()
        {
            var source = new Win32WindowSource();

            Assert.Empty(source.LastEnumerated);
            Assert.Equal(0, CachedImages(source));
        }

        [Fact]
        public void WindowSource_Trim_WithNothingCached_DoesNothing()
        {
            var source = new Win32WindowSource();

            source.Trim(new nint[] { 1, 2, 3 });
            source.Trim(Array.Empty<nint>());

            Assert.Equal(0, CachedImages(source));
            Assert.Equal(0, Count(source, "inFlight"));
        }

        [Fact]
        public void WindowSource_Capture_ReturnsWhatTheCallGave_OnItsOwnTask()
        {
            using var printer = new Printer();
            int caller = Environment.CurrentManagedThreadId;
            int printedOn = caller;
            var source = new Win32WindowSource(_w =>
            {
                printedOn = Environment.CurrentManagedThreadId;
                return printer.Print(_w);
            });

            var image = source.Capture(Window(7, 2, 1), longWait);

            Assert.Equal(CaptureFailure.None, image.Failure);
            Assert.Same(printer.Image(7), image.Image);
            Assert.NotEqual(caller, printedOn); // a hung window must not block the capture thread itself
            Assert.Equal(CaptureFailure.None, source.Capture(Window(7, 2, 1), longWait).Failure);
            Assert.Equal(2, printer.Calls(7));
        }

        [Fact]
        public void WindowSource_Capture_HungWindow_TimesOutAfterTheTimeout()
        {
            using var printer = new Printer();
            printer.Hold(1);
            var source = new Win32WindowSource(printer.Print);
            var clock = Stopwatch.StartNew();

            var image = source.Capture(Window(1, 16, 8), TimeSpan.FromMilliseconds(50));

            clock.Stop();
            Assert.Equal(CaptureFailure.Timeout, image.Failure);
            Assert.Null(image.Image);
            // Task.Wait gives up on a timer: allow it the timer's granularity, but not an early return.
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(40), $"returned after {clock.Elapsed.TotalMilliseconds} ms");
        }

        [Fact]
        public void WindowSource_Capture_WhileTheEarlierCallHangs_TimesOutAtOnce_WithoutCallingAgain()
        {
            using var printer = new Printer();
            printer.Hold(1);
            var source = new Win32WindowSource(printer.Print);
            var window = Window(1, 16, 8);
            Assert.Equal(CaptureFailure.Timeout, source.Capture(window, TimeSpan.FromMilliseconds(1)).Failure);
            Assert.True(printer.Entered.Wait(longWait, TestContext.Current.CancellationToken)); // the abandoned call is inside PrintWindow now
            var clock = Stopwatch.StartNew();

            var image = source.Capture(window, longWait);

            clock.Stop();
            Assert.Equal(CaptureFailure.Timeout, image.Failure);
            Assert.Null(image.Image);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"waited {clock.Elapsed.TotalMilliseconds} ms of the 30 s timeout");
            Assert.Equal(1, printer.Calls(1)); // no second blocked thread, DC and DIB section
        }

        [Fact]
        public void WindowSource_Capture_OtherWindow_IsCapturedWhileOneHangs()
        {
            using var printer = new Printer();
            printer.Hold(1);
            var source = new Win32WindowSource(printer.Print);
            Assert.Equal(CaptureFailure.Timeout, source.Capture(Window(1, 16, 8), TimeSpan.FromMilliseconds(1)).Failure);

            var image = source.Capture(Window(2, 16, 8), longWait);

            Assert.Equal(CaptureFailure.None, image.Failure);
            Assert.Same(printer.Image(2), image.Image);
            Assert.Equal(1, printer.Calls(2));
        }

        [Fact]
        public async Task WindowSource_Trim_KeepsAHungCall_UntilItReturns()
        {
            using var printer = new Printer();
            printer.Hold(1);
            var source = new Win32WindowSource(printer.Print);
            Assert.Equal(CaptureFailure.Timeout, source.Capture(Window(1, 16, 8), TimeSpan.FromMilliseconds(1)).Failure);
            Assert.Equal(CaptureFailure.None, source.Capture(Window(2, 16, 8), longWait).Failure);
            Assert.Equal(2, Count(source, "inFlight"));

            // Both windows are gone: the finished call is forgotten, the hung one is kept (it still blocks a new call).
            source.Trim(Array.Empty<nint>());
            Assert.Equal(1, Count(source, "inFlight"));
            Assert.False(InFlight(source, 1).IsCompleted);

            printer.Release(1);
            await InFlight(source, 1).WaitAsync(longWait, TestContext.Current.CancellationToken);
            source.Trim(new nint[] { 1 }); // finished, but the window is alive: kept
            Assert.Equal(1, Count(source, "inFlight"));
            source.Trim(Array.Empty<nint>());
            Assert.Equal(0, Count(source, "inFlight"));
        }

        [Fact]
        public async Task WindowSource_Capture_AfterTheHungCallReturned_CallsAgain()
        {
            using var printer = new Printer();
            printer.Hold(1);
            var source = new Win32WindowSource(printer.Print);
            var window = Window(1, 16, 8);
            Assert.Equal(CaptureFailure.Timeout, source.Capture(window, TimeSpan.FromMilliseconds(1)).Failure);
            printer.Release(1);
            await InFlight(source, 1).WaitAsync(longWait, TestContext.Current.CancellationToken);

            var image = source.Capture(window, longWait);

            Assert.Equal(CaptureFailure.None, image.Failure);
            Assert.Same(printer.Image(1), image.Image);
            Assert.Equal(2, printer.Calls(1));
        }

        [Fact]
        public void WindowSource_Capture_CallThatThrows_IsUnavailable_AndTheNextCallRuns()
        {
            // Decided behaviour: the window fails like one PrintWindow cannot read (the composer ends the capture with that
            // failure) instead of an AggregateException out of the whole capture, which skipped the caches' trimming.
            using var printer = new Printer { Throw = new InsufficientMemoryException("boom") };
            var source = new Win32WindowSource(printer.Print);
            var window = Window(1, 16, 8);

            var failed = source.Capture(window, longWait);

            Assert.Equal(CaptureFailure.Unavailable, failed.Failure);
            Assert.Null(failed.Image);
            printer.Throw = null;
            var image = source.Capture(window, longWait); // the faulted call is finished: it does not block the window
            Assert.Equal(CaptureFailure.None, image.Failure);
            Assert.Equal(2, printer.Calls(1));
        }

        [Fact]
        public void WindowSource_Capture_CallThatThrows_ThroughTheComposer_FailsTheCapture()
        {
            using var printer = new Printer { Throw = new InvalidOperationException("boom") };
            var source = new Win32WindowSource(printer.Print);
            var composer = new UnderlayComposer(new ListedSource(source, Window(1, 16, 8)));
            var output = new FrameBuffer();

            var result = composer.Compose(new CaptureRequest(new PhysicalRect(0, 0, 16, 8), Array.Empty<nint>(), 0), output);

            Assert.Equal(CaptureFailure.Unavailable, result.Failure);
            Assert.Equal(new nint[] { 1 }, composer.LastCaptured);
        }

        /// <summary>The source with a fixed window list (EnumWindows is native).</summary>
        private sealed class ListedSource : IWindowSource
        {
            private readonly Win32WindowSource source;
            private readonly WindowInfo[] windows;

            public ListedSource(Win32WindowSource _source, params WindowInfo[] _windows)
            {
                source = _source;
                windows = _windows;
            }

            public IReadOnlyList<WindowInfo> EnumerateTopDown() => windows;

            public WindowImage Capture(WindowInfo _window, TimeSpan _timeout) => source.Capture(_window, _timeout);
        }

        [Fact(Skip = OsOnly.EnumWindows, SkipUnless = nameof(IsWindows))]
        public void WindowSource_Enumerate_ListsVisibleWindows()
        {
            var source = new Win32WindowSource();

            var windows = source.EnumerateTopDown();

            Assert.NotEmpty(source.LastEnumerated); // a desktop session always has top-level windows (hidden ones included)
            Assert.True(source.LastEnumerated.Count >= windows.Count);
            foreach (var window in windows)
            {
                Assert.Contains(window.Handle, source.LastEnumerated);
                Assert.True(window.Visible);
                Assert.True(window.WindowRect.Width >= 0 && window.WindowRect.Height >= 0);
                Assert.NotNull(window.ClassName);
                Assert.InRange(window.ClassName.Length, 0, 255);
            }
        }

        [Fact(Skip = OsOnly.EnumWindows, SkipUnless = nameof(IsWindows))]
        public void WindowSource_Enumerate_IsInZOrder_TopmostFirst()
        {
            // HWND_BOTTOM is always allowed (bringing a window up may be refused to a background process).
            const nint bottom = 1;
            const uint noSize = 0x0001, noMove = 0x0002;
            uint flags = noSize | noMove | Win32Native.SWP_NOACTIVATE;
            using var a = new TestWindow(10, 10);
            using var b = new TestWindow(20, 20);
            var source = new Win32WindowSource();

            Assert.True(Win32Native.SetWindowPos(a.Handle, bottom, 0, 0, 0, 0, flags));
            var order = source.EnumerateTopDown().Select(_w => _w.Handle).ToList();
            Assert.True(order.IndexOf(b.Handle) >= 0 && order.IndexOf(b.Handle) < order.IndexOf(a.Handle));

            Assert.True(Win32Native.SetWindowPos(b.Handle, bottom, 0, 0, 0, 0, flags));
            order = source.EnumerateTopDown().Select(_w => _w.Handle).ToList();
            Assert.True(order.IndexOf(a.Handle) >= 0 && order.IndexOf(a.Handle) < order.IndexOf(b.Handle));
        }

        [Fact(Skip = OsOnly.PrintWindow, SkipUnless = nameof(IsWindows))]
        public void WindowSource_Capture_EmptyWindow_IsUnavailable()
        {
            // CreateDIBSection refuses a 0 x 0 bitmap.
            var source = new Win32WindowSource();

            var image = source.Capture(Window(0x7FFFFFF0, 0, 0), longWait);

            Assert.Equal(CaptureFailure.Unavailable, image.Failure);
            Assert.Null(image.Image);
        }

        [Fact(Skip = OsOnly.PrintWindow, SkipUnless = nameof(IsWindows))]
        public void WindowSource_Capture_GoneWindow_IsBlack_AndNotCached()
        {
            var source = new Win32WindowSource();

            var image = source.Capture(Window(0x7FFFFFF0, 16, 8), longWait);

            Assert.Equal(CaptureFailure.Black, image.Failure);
            Assert.Null(image.Image);
            Assert.Equal(0, CachedImages(source));
        }

        [Fact(Skip = OsOnly.PrintWindow, SkipUnless = nameof(IsWindows))]
        public void WindowSource_OwnWindow_IsListed_AndCopiedTopDownInBgra_ThenForgotten()
        {
            using var own = new TestWindow(40, 60);
            var source = new Win32WindowSource();

            var info = Assert.Single(source.EnumerateTopDown(), _w => _w.Handle == own.Handle);
            Assert.Equal(new PhysicalRect(40, 60, TestWindow.Width, TestWindow.Height), info.WindowRect);
            Assert.True(info.Visible);
            Assert.False(info.Cloaked);
            Assert.Equal(own.ClassName, info.ClassName);

            var image = source.Capture(info, longWait);
            for (int i = 0; i < 20 && !(image.Failure == CaptureFailure.None && image.Image!.Bgra.Span[2] == 0xFF); i++)
            {
                // Painted synchronously at creation; DWM may still be composing it the first time.
                _ = TestWindow.DwmFlush();
                image = source.Capture(info, longWait);
            }

            Assert.Equal(CaptureFailure.None, image.Failure);
            var frame = image.Image!;
            Assert.Equal(TestWindow.Width, frame.Width);
            Assert.Equal(TestWindow.Height, frame.Height);
            Assert.Equal(TestWindow.Width * 4, frame.Stride);
            Assert.Equal(TestWindow.Width * TestWindow.Height * 4, frame.Bgra.Length);
            var px = frame.Bgra.Span;
            for (int y = 0; y < TestWindow.Height; y++)
            {
                // Top to bottom (the DIB has a negative height), BGRA: red rows 0..15, blue rows 16..31, every column copied.
                // The alpha byte is left as GDI wrote it: the composer makes every pixel opaque.
                byte[] bgr = y < TestWindow.Height / 2 ? new byte[] { 0x00, 0x00, 0xFF } : new byte[] { 0xFF, 0x00, 0x00 };
                for (int x = 0; x < TestWindow.Width; x++)
                {
                    int i = y * frame.Stride + x * 4;
                    Assert.True(px[i] == bgr[0] && px[i + 1] == bgr[1] && px[i + 2] == bgr[2],
                        $"pixel ({x}, {y}) is {px[i]:X2} {px[i + 1]:X2} {px[i + 2]:X2}");
                }
            }
            Assert.Equal(1, CachedImages(source));
            Assert.Same(frame, source.Capture(info, longWait).Image); // the buffer is reused per window

            own.Destroy();
            var gone = source.Capture(info, longWait);
            Assert.Equal(CaptureFailure.Black, gone.Failure);
            Assert.DoesNotContain(source.EnumerateTopDown(), _w => _w.Handle == own.Handle);
            source.Trim(source.LastEnumerated);
            Assert.Equal(0, CachedImages(source));
            Assert.Equal(0, Count(source, "inFlight"));
        }

        [Fact(Skip = OsOnly.PrintWindow, SkipUnless = nameof(IsWindows))]
        public void WindowSource_Capture_RealWindow_CopiesItsBitmap_AndTrimForgetsIt()
        {
            var source = new Win32WindowSource();
            var window = source.EnumerateTopDown().FirstOrDefault(_w => _w.WindowRect.Width is > 0 and < 4096 && _w.WindowRect.Height is > 0 and < 4096);
            Assert.NotNull(window);

            var image = source.Capture(window!, longWait);

            // A window may legitimately refuse (Black); when it answers, the copy has its size.
            Assert.Contains(image.Failure, new[] { CaptureFailure.None, CaptureFailure.Black });
            if (image.Failure == CaptureFailure.None)
            {
                Assert.Equal(window!.WindowRect.Width, image.Image!.Width);
                Assert.Equal(window.WindowRect.Height, image.Image.Height);
                Assert.Equal(1, CachedImages(source));
                var again = source.Capture(window, longWait);
                Assert.Same(image.Image, again.Image); // the buffer is reused per window
            }
            source.Trim(new[] { window!.Handle });
            Assert.Equal(image.Failure == CaptureFailure.None ? 1 : 0, CachedImages(source));
            source.Trim(Array.Empty<nint>());
            Assert.Equal(0, CachedImages(source));
            Assert.Equal(0, Count(source, "inFlight")); // finished calls of gone windows are forgotten too
        }

        [Fact(Skip = OsOnly.PrintWindow, SkipUnless = nameof(IsWindows))]
        public async Task UnderlayCapture_ComposesTheDesktopCorner_AndKeepsOneBufferPerLiveArea()
        {
            using var capture = new Win32UnderlayCapture();
            var rect = new PhysicalRect(0, 0, 64, 32);

            var first = await capture.CaptureAsync(new CaptureRequest(rect, new nint[] { 101 }, 0), CancellationToken.None);
            Assert.Equal(1, Count(capture, "outputs"));
            var second = await capture.CaptureAsync(new CaptureRequest(rect, new nint[] { 202, 101 }, 0), CancellationToken.None);
            // Keyed by the requesting area's window (the first handle): 202 gets its own, and 101 is still listed as alive.
            Assert.Equal(2, Count(capture, "outputs"));
            var third = await capture.CaptureAsync(new CaptureRequest(rect, new nint[] { 101 }, 0), CancellationToken.None);

            Assert.Equal(1, Count(capture, "outputs")); // 202 (a deleted area) is forgotten
            Assert.All(new[] { first, second, third }, _r => Assert.True(_r.Failure is CaptureFailure.None or CaptureFailure.Black or CaptureFailure.Timeout));
            if (third.Failure == CaptureFailure.None)
            {
                Assert.Equal(64, third.Frame!.Width);
                Assert.Equal(32, third.Frame.Height);
            }
        }

        [Fact(Skip = OsOnly.PrintWindow, SkipUnless = nameof(IsWindows))]
        public async Task UnderlayCapture_OwnWindow_IsComposedUnderAnArea()
        {
            // The whole Windows capture on a window the test owns: the area over its middle gets red over blue, opaque.
            using var own = new TestWindow(200, 150, true); // topmost: no other window of the runner covers it
            using var capture = new Win32UnderlayCapture();
            var rect = new PhysicalRect(200 + 8, 150 + 8, 16, 16);
            _ = TestWindow.DwmFlush();

            var result = await capture.CaptureAsync(new CaptureRequest(rect, new nint[] { 0x7FFFFFF0 }, 0), CancellationToken.None);

            Assert.Equal(CaptureFailure.None, result.Failure);
            var px = result.Frame!.Bgra.Span;
            int Pixel(int _x, int _y) => (_y * result.Frame.Stride) + (_x * 4);
            Assert.Equal(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, px.Slice(Pixel(0, 0), 4).ToArray());   // window row 8: red
            Assert.Equal(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, px.Slice(Pixel(15, 7), 4).ToArray());  // row 15: red
            Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0xFF }, px.Slice(Pixel(0, 8), 4).ToArray());   // row 16: blue
            Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0xFF }, px.Slice(Pixel(15, 15), 4).ToArray()); // row 23: blue
        }

        [Fact(Skip = OsOnly.PrintWindow, SkipUnless = nameof(IsWindows))]
        public async Task UnderlayCapture_WithoutExcludedWindows_UsesOneSharedBuffer()
        {
            using var capture = new Win32UnderlayCapture();

            await capture.CaptureAsync(new CaptureRequest(new PhysicalRect(0, 0, 16, 16), Array.Empty<nint>(), 0), CancellationToken.None);

            // Key 0 is not in the (empty) list of live windows: it is trimmed right after the capture.
            Assert.Equal(0, Count(capture, "outputs"));
        }
    }
}
