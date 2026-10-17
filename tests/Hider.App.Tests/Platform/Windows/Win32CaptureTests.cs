using System.Collections;
using System.Reflection;
using System.Runtime.Versioning;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Platform.Windows;

namespace Hider.App.Tests.Platform.Windows
{
    /// <summary>
    /// Win32WindowSource and Win32UnderlayCapture. What runs before any native call (traits, cancellation, the caches) is
    /// checked on every OS; enumeration and PrintWindow on Windows only.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class Win32CaptureTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        private static int CachedImages(Win32WindowSource _source) => Count(_source, "images");

        private static int Count(object _owner, string _field)
        {
            var map = (IDictionary)_owner.GetType().GetField(_field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_owner)!;
            return map.Count;
        }

        private static WindowInfo Window(nint _handle, int _width, int _height)
        {
            var rect = new PhysicalRect(0, 0, _width, _height);
            return new WindowInfo(_handle, rect, rect, true, false, "test");
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

        [Fact(Skip = "Windows 専用（EnumWindows / DWM）", SkipUnless = nameof(IsWindows))]
        public void WindowSource_Enumerate_ListsVisibleWindowsInZOrder()
        {
            var source = new Win32WindowSource();

            var windows = source.EnumerateTopDown();

            Assert.NotEmpty(source.LastEnumerated); // a desktop session always has top-level windows (hidden ones included)
            Assert.True(source.LastEnumerated.Count >= windows.Count);
            var order = source.LastEnumerated.ToList();
            int previous = -1;
            foreach (var window in windows)
            {
                int index = order.IndexOf(window.Handle);
                Assert.True(index > previous); // same order as EnumWindows: topmost first
                previous = index;
                Assert.True(window.Visible);
                Assert.True(window.WindowRect.Width >= 0 && window.WindowRect.Height >= 0);
                Assert.NotNull(window.ClassName);
                Assert.InRange(window.ClassName.Length, 0, 255);
            }
        }

        [Fact(Skip = "Windows 専用（PrintWindow）", SkipUnless = nameof(IsWindows))]
        public void WindowSource_Capture_EmptyWindow_IsUnavailable()
        {
            // CreateDIBSection refuses a 0 x 0 bitmap.
            var source = new Win32WindowSource();

            var image = source.Capture(Window(0x7FFFFFF0, 0, 0), TimeSpan.FromSeconds(30));

            Assert.Equal(CaptureFailure.Unavailable, image.Failure);
            Assert.Null(image.Image);
        }

        [Fact(Skip = "Windows 専用（PrintWindow）", SkipUnless = nameof(IsWindows))]
        public void WindowSource_Capture_GoneWindow_IsBlack_AndNotCached()
        {
            var source = new Win32WindowSource();

            var image = source.Capture(Window(0x7FFFFFF0, 16, 8), TimeSpan.FromSeconds(30));

            Assert.Equal(CaptureFailure.Black, image.Failure);
            Assert.Null(image.Image);
            Assert.Equal(0, CachedImages(source));
        }

        [Fact(Skip = "Windows 専用（PrintWindow）", SkipUnless = nameof(IsWindows))]
        public void WindowSource_Capture_RealWindow_CopiesItsBitmap_AndTrimForgetsIt()
        {
            var source = new Win32WindowSource();
            var window = source.EnumerateTopDown().FirstOrDefault(_w => _w.WindowRect.Width is > 0 and < 4096 && _w.WindowRect.Height is > 0 and < 4096);
            Assert.NotNull(window);

            var image = source.Capture(window!, TimeSpan.FromSeconds(30));

            // A window may legitimately refuse (Black); when it answers, the copy has its size.
            Assert.Contains(image.Failure, new[] { CaptureFailure.None, CaptureFailure.Black });
            if (image.Failure == CaptureFailure.None)
            {
                Assert.Equal(window!.WindowRect.Width, image.Image!.Width);
                Assert.Equal(window.WindowRect.Height, image.Image.Height);
                Assert.Equal(1, CachedImages(source));
                var again = source.Capture(window, TimeSpan.FromSeconds(30));
                Assert.Same(image.Image, again.Image); // the buffer is reused per window
            }
            source.Trim(new[] { window!.Handle });
            Assert.Equal(image.Failure == CaptureFailure.None ? 1 : 0, CachedImages(source));
            source.Trim(Array.Empty<nint>());
            Assert.Equal(0, CachedImages(source));
            Assert.Equal(0, Count(source, "inFlight")); // finished calls of gone windows are forgotten too
        }

        [Fact(Skip = "Windows 専用（PrintWindow）", SkipUnless = nameof(IsWindows))]
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

        [Fact(Skip = "Windows 専用（PrintWindow）", SkipUnless = nameof(IsWindows))]
        public async Task UnderlayCapture_WithoutExcludedWindows_UsesOneSharedBuffer()
        {
            using var capture = new Win32UnderlayCapture();

            await capture.CaptureAsync(new CaptureRequest(new PhysicalRect(0, 0, 16, 16), Array.Empty<nint>(), 0), CancellationToken.None);

            // Key 0 is not in the (empty) list of live windows: it is trimmed right after the capture.
            Assert.Equal(0, Count(capture, "outputs"));
        }
    }
}
