using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.App.Tests.Areas
{
    public class PresenterTests
    {
        private static SKBitmap Capture(AreaController _area)
        {
            _area.Window.PatternView.InvalidateVisual();
            using var frame = _area.Window.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            var colorType = buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            var info = new SKImageInfo(buffer.Size.Width, buffer.Size.Height, colorType, SKAlphaType.Premul);
            var bytes = new byte[buffer.RowBytes * info.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            using var image = SKImage.FromPixelCopy(info, bytes, buffer.RowBytes);
            return Render.ToBitmap(image);
        }

        [AvaloniaFact]
        public void GT21_PresenterOffscreen_EqualsCoreDraw()
        {
            using var f = new AreaFixture();
            var area = f.Add(new PhysicalRect(0, 0, SyntheticImage.Width, SyntheticImage.Height), PatternKind.Mosaic);
            area.ExitEdit();
            area.Refresh();
            using var actual = Capture(area);
            using var expected = Render.Mosaic("pixelate");
            Assert.Equal(expected.Width, actual.Width);
            Assert.True(Golden.MaxDiff(expected, actual) <= 1, $"presenter differs from the Core pipeline by {Golden.MaxDiff(expected, actual)}/255");
            Assert.True(area.Window.PatternView.MosaicRenderCount >= 1);
        }

        [AvaloniaFact]
        public void GT22_Fill50Percent_WindowOpacity()
        {
            using var f = new AreaFixture();
            var area = f.Add(new PhysicalRect(100, 100, 200, 100), PatternKind.Fill);
            area.Model.Fill.Color = "#68768A";
            area.Model.Opacity = 0.5;
            area.ExitEdit();
            Assert.Equal(0.5, area.Window.Opacity, 6);
            using var pixels = Capture(area);
            // Drawn at 50 % alpha: unpremultiplying can be off by one.
            var c = pixels.GetPixel(50, 50);
            Assert.InRange(c.Red, 0x67, 0x69);
            Assert.InRange(c.Green, 0x75, 0x77);
            Assert.InRange(c.Blue, 0x89, 0x8B);
        }

        [AvaloniaFact]
        public void GT23_OpacityAndBlockChanges_DoNotCapture()
        {
            using var f = new AreaFixture();
            var capture = (FakeUnderlayCapture)f.Capture;
            var area = f.Add(new PhysicalRect(0, 0, 200, 120), PatternKind.Mosaic);
            area.ExitEdit();
            area.Refresh();
            int calls = capture.Calls;
            Capture(area).Dispose();
            int renders = area.Window.PatternView.MosaicRenderCount;

            area.Model.Opacity = 0.4;
            area.SettingsChanged();
            Capture(area).Dispose();
            area.Model.Mosaic.ParamsFor("pixelate")["block"] = 20;
            area.SettingsChanged();
            Capture(area).Dispose();

            Assert.Equal(calls, capture.Calls);
            Assert.True(area.Window.PatternView.MosaicRenderCount >= renders + 2, "redrawn from the same capture");
        }

        [AvaloniaTheory]
        [InlineData(CaptureFailure.Black)]
        [InlineData(CaptureFailure.Denied)]
        [InlineData(CaptureFailure.Unavailable)]
        public void GT24_CaptureFailure_FallsBackToGenerated(CaptureFailure _failure)
        {
            var capture = new FakeUnderlayCapture { Failure = _failure };
            using var f = new AreaFixture(_capture: capture);
            var area = f.Manager.AddNew();
            area.Model.Mosaic.ParamsFor("pixelate")["block"] = 33;
            Assert.Equal(PatternKind.Generated, area.Model.Pattern);
            Assert.True(area.CaptureFailed);
            Assert.True(area.Window.Toolbar.FailedText.IsVisible);
            Assert.Equal("取得できません", area.Window.Toolbar.FailedText.Text);
            Assert.True(area.WindowRect.Width >= 8 + EditToolbar.WidthWithWarning, "the wider toolbar fits in the window");
            Assert.Equal(33, area.Model.Mosaic.Params["pixelate"]["block"]);
            Assert.Equal("pixelate", area.Model.Mosaic.Method);

            // Choosing mosaic again tries again (and clears the warning when it works).
            capture.Failure = CaptureFailure.None;
            area.SetPattern(PatternKind.Mosaic);
            Assert.False(area.CaptureFailed);
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
        }

        [AvaloniaFact]
        public void GT25_GeneratedCache_OnlyRedrawsOnResize()
        {
            using var f = new AreaFixture();
            var area = f.Add(new PhysicalRect(100, 100, 240, 160), PatternKind.Generated);
            area.ExitEdit();
            Capture(area).Dispose();
            int renders = area.Window.PatternView.GeneratedRenderCount;
            Capture(area).Dispose();
            area.Window.PatternView.InvalidateVisual();
            Capture(area).Dispose();
            Assert.Equal(renders, area.Window.PatternView.GeneratedRenderCount);

            area.Model.Rect = area.Model.Rect with { Width = 300 };
            area.Layout();
            Capture(area).Dispose();
            Assert.Equal(renders + 1, area.Window.PatternView.GeneratedRenderCount);
        }
    }
}
