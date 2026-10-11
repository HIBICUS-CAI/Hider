using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using SkiaSharp;

namespace Hider.App.Tests.Areas
{
    public class StripHostTests
    {
        private static SKBitmap Render(AreaController _area)
        {
            using var frame = _area.Window.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            var info = new SKImageInfo(buffer.Size.Width, buffer.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);
            var bytes = new byte[buffer.RowBytes * info.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), Math.Min(bytes.Length, info.BytesSize));
            return bitmap;
        }

        private static void Wait(Task _task)
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!_task.IsCompleted && DateTime.UtcNow < until)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(_task.IsCompleted, "strip transparency never completed");
            _task.GetAwaiter().GetResult();
        }

        [AvaloniaTheory]
        [InlineData(AreaMode.Hidden)]
        [InlineData(AreaMode.Editing)]
        public void TransparentRows_HaveAlphaZero_RestoreBringsThemBack(AreaMode _mode)
        {
            using var f = new AreaFixture();
            var area = f.Add(new PhysicalRect(100, 200, 120, 80), PatternKind.Generated);
            if (_mode == AreaMode.Hidden)
            {
                area.ExitEdit();
            }
            var offset = area.AreaOffsetInWindow;
            int x = offset.X + 60;

            Wait(area.Window.SetTransparentRowsAsync(16, 24, TestContext.Current.CancellationToken));
            Assert.Equal((16, 24), area.Window.TransparentRows);
            using (var strip = Render(area))
            {
                for (int y = 16; y < 24; y++)
                {
                    Assert.Equal(0, strip.GetPixel(x, offset.Y + y).Alpha);
                }
                // Rows next to the strip keep the pattern (one px of margin on each side is allowed to fade).
                Assert.Equal(255, strip.GetPixel(x, offset.Y + 13).Alpha);
                Assert.Equal(255, strip.GetPixel(x, offset.Y + 26).Alpha);
            }

            Wait(area.Window.RestoreRowsAsync());
            Assert.Null(area.Window.TransparentRows);
            using var restored = Render(area);
            for (int y = 16; y < 24; y++)
            {
                Assert.Equal(255, restored.GetPixel(x, offset.Y + y).Alpha);
            }
        }

        [AvaloniaFact]
        public void Coordinator_DrivesTheAreaWindows()
        {
            // StripCoordinator → AreaStripHost → AreaWindow: overlapping mosaic windows are see-through exactly while a grab runs.
            var log = new List<string>();
            var grabber = new FakeScreenGrabber(log, CoordinateFrame.Create(1920, 1080));
            var bridge = new AreaStripHost(f => Dispatcher.UIThread.InvokeAsync(f));
            using var coordinator = new StripCoordinator(grabber, bridge, () => bridge.Areas);
            using var f = new AreaFixture(_capture: new UnavailableCapture(), _isWindows: false);
            bridge.Attach(f.Manager);
            var me = f.Add(new PhysicalRect(100, 200, 120, 16), PatternKind.Mosaic);
            var over = f.Add(new PhysicalRect(150, 208, 120, 80), PatternKind.Mosaic);
            var fill = f.Add(new PhysicalRect(100, 200, 40, 40), PatternKind.Fill);
            var seen = new List<string>();
            grabber.OnGrab = () => seen.Add($"{me.Window.TransparentRows}|{over.Window.TransparentRows}|{fill.Window.TransparentRows}");

            f.Manager.CaptureStarting();
            var task = coordinator.CaptureAsync(new CaptureRequest(me.Model.Rect, Array.Empty<nint>(), 8), TestContext.Current.CancellationToken);
            Wait(task);

            Assert.Equal(new[] { "(0, 8)||", "(8, 16)|(0, 8)|" }, seen);
            Assert.Null(me.Window.TransparentRows);
            Assert.Null(over.Window.TransparentRows);
            Assert.Equal(new PhysicalRect(0, 0, 1920, 1080), f.Manager.StripScreen);
            var frame = task.Result.Frame!;
            Assert.Equal((100, 200), CoordinateFrame.Decode(frame, 0, 0));
            Assert.Equal((219, 215), CoordinateFrame.Decode(frame, 119, 15));
        }
    }
}
