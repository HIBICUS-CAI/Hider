using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Hider.Areas;
using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// When a pattern cannot be drawn the area is filled with its fill color, opaque, never left see-through (NFR-05): a draw
    /// that throws (also after drawing part of the area), no Skia lease, a lease whose canvas fails, the window closing. The
    /// pattern itself is drawn as before once it can be.
    /// </summary>
    public class MosaicPresenterCoverTests
    {
        private const int W = 64, H = 48;
        private static readonly SKColor defaultFill = new(0x68, 0x76, 0x8A);

        private static Area NewArea(PatternKind _pattern, string _fill = AreaDefaults.FillColor)
        {
            var area = Area.CreateNew("0f3b8d2e", new PhysicalRect(10, 20, W, H), true);
            Hider.Core.Persistence.AreasFile.FillDefaults(area.Mosaic.Params, Hider.Core.Mosaic.MethodKind.Mosaic);
            Hider.Core.Persistence.AreasFile.FillDefaults(area.Generated.Params, Hider.Core.Mosaic.MethodKind.Generated);
            area.Pattern = _pattern;
            area.Fill.Color = _fill;
            return area;
        }

        private static void Publish(FrameExchange _frames, int _w, int _h)
        {
            _frames.Back.Resize(_w, _h);
            SyntheticImage.CreatePixels(_w, _h).AsSpan().CopyTo(_frames.Back.Bgra.Span);
            _frames.Publish();
        }

        private static FrameExchange GoodFrame()
        {
            var frames = new FrameExchange();
            Publish(frames, W, H);
            return frames;
        }

        /// <summary>A front frame without pixels: no capture makes one, and drawing a mosaic of it throws.</summary>
        private static FrameExchange BrokenFrame()
        {
            var frames = new FrameExchange();
            frames.Back.Resize(0, 0);
            frames.Publish();
            return frames;
        }

        /// <summary>Lays the presenter out at <paramref name="_w"/> x <paramref name="_h"/> and draws it into a bitmap.</summary>
        private static SKBitmap Draw(MosaicPresenter _presenter, double _w = W, double _h = H, int _pw = W, int _ph = H)
        {
            _presenter.Measure(new Size(_w, _h));
            _presenter.Arrange(new Rect(0, 0, _w, _h));
            using var target = new RenderTargetBitmap(new PixelSize(_pw, _ph));
            using (var context = target.CreateDrawingContext())
            {
                _presenter.Render(context);
            }
            var colorType = target.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            using var bitmap = new SKBitmap(new SKImageInfo(_pw, _ph, colorType, SKAlphaType.Premul));
            target.CopyPixels(new PixelRect(0, 0, _pw, _ph), bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
            using var image = SKImage.FromBitmap(bitmap);
            return Render.ToBitmap(image);
        }

        /// <summary>The pixels in [0, w) x [0, h) that are not exactly <paramref name="_color"/> (opaque).</summary>
        private static int CountOther(SKBitmap _b, SKColor _color, int _w = W, int _h = H)
        {
            int n = 0;
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    if (_b.GetPixel(x, y) != _color)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        private static bool AllCovered(SKBitmap _b, SKColor _color) => CountOther(_b, _color, _b.Width, _b.Height) == 0;

        /// <summary>
        /// A Skia lease on a raster surface of its own. Its canvas throws on the first <c>failures</c> reads (negative: on
        /// every read), standing in for a canvas that fails mid-draw.
        /// </summary>
        private sealed class FakeLease : ISkiaSharpApiLease
        {
            private readonly SKSurface surface;
            private int failures;

            public FakeLease(int _failures, SKColor _background)
            {
                failures = _failures;
                surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul))!;
                surface.Canvas.Clear(_background);
            }

            public int Disposals { get; private set; }

            public int CanvasReads { get; private set; }

            public SKCanvas SkCanvas
            {
                get
                {
                    CanvasReads++;
                    if (failures != 0)
                    {
                        failures--;
                        throw new InvalidOperationException("canvas lost");
                    }
                    return surface.Canvas;
                }
            }

            public GRContext GrContext => null!;

            public SKSurface SkSurface => surface;

            public double CurrentOpacity => 1;

            public ISkiaSharpPlatformGraphicsApiLease TryLeasePlatformGraphicsApi() => null!;

            public SKBitmap Pixels()
            {
                var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
                surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
                return bitmap;
            }

            public void Dispose()
            {
                Disposals++;
            }

            public void Free()
            {
                surface.Dispose();
            }
        }

        [AvaloniaTheory]
        [InlineData("#68768A", 0x68, 0x76, 0x8A)]
        [InlineData("#000000", 0x00, 0x00, 0x00)]
        [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
        [InlineData("#ff8000", 0xFF, 0x80, 0x00)]   // lower case is the same color
        [InlineData("not a color", 0x68, 0x76, 0x8A)] // an invalid one falls back to the default fill color
        public void MosaicDrawThatThrows_CoversTheWholeAreaWithTheFillColor_Opaque(string _fill, byte _r, byte _g, byte _b)
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic, _fill), Frames = BrokenFrame() };
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, new SKColor(_r, _g, _b)));
            Assert.Equal(0, presenter.MosaicRenderCount);
        }

        [AvaloniaTheory]
        [InlineData(0.0)]  // mosaic allows 0 (the mosaic is see-through by the user's choice), the cover never is
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void Cover_IsOpaque_WhateverTheOpacity(double _opacity)
        {
            var area = NewArea(PatternKind.Mosaic);
            area.Opacity = _opacity;
            using var presenter = new MosaicPresenter { Model = area, Frames = BrokenFrame() };
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, defaultFill));
        }

        [AvaloniaFact]
        public void Cover_UsesTheFillColorOfThisDraw()
        {
            var area = NewArea(PatternKind.Mosaic, "#112233");
            using var presenter = new MosaicPresenter { Model = area, Frames = BrokenFrame() };
            using (var first = Draw(presenter))
            {
                Assert.True(AllCovered(first, new SKColor(0x11, 0x22, 0x33)));
            }
            area.Fill.Color = "#445566";
            using var second = Draw(presenter);
            Assert.True(AllCovered(second, new SKColor(0x44, 0x55, 0x66)));
        }

        [AvaloniaFact]
        public void Cover_FillsTheBoundsOnly()
        {
            // A presenter smaller than the target: the cover is its rectangle, nothing outside.
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = BrokenFrame() };
            using var bitmap = Draw(presenter, 40, 30, W, H);
            Assert.Equal(0, CountOther(bitmap, defaultFill, 40, 30));
            Assert.Equal(0, bitmap.GetPixel(40, 0).Alpha);
            Assert.Equal(0, bitmap.GetPixel(0, 30).Alpha);
            Assert.Equal(0, bitmap.GetPixel(W - 1, H - 1).Alpha);
        }

        [AvaloniaFact]
        public void AfterAFailure_TheNextGoodFrameDrawsTheMosaicAgain()
        {
            var frames = BrokenFrame();
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = frames };
            using (var covered = Draw(presenter))
            {
                Assert.True(AllCovered(covered, defaultFill));
            }
            Publish(frames, W, H);
            using var bitmap = Draw(presenter);
            Assert.Equal(1, presenter.MosaicRenderCount);
            Assert.False(AllCovered(bitmap, defaultFill));
            Assert.Equal(255, bitmap.GetPixel(W / 2, H / 2).Alpha);
        }

        [AvaloniaFact]
        public void DrawFailure_IsLogged()
        {
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = BrokenFrame() };
                Draw(presenter).Dispose();
            }
            Assert.Contains("pattern draw failed: ", log.ToString(), StringComparison.Ordinal);
            Assert.Contains("; the area is covered with its fill color", log.ToString(), StringComparison.Ordinal);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void FailureAfterPartOfTheAreaWasDrawn_TheCoverReplacesIt(PatternKind _pattern)
        {
            // The canvas already holds other pixels (a half-drawn pattern) when the draw fails: the cover replaces them all.
            var lease = new FakeLease(1, SKColors.Red);
            try
            {
                using var presenter = new MosaicPresenter { Model = NewArea(_pattern), Frames = GoodFrame(), LeaseSkia = _ => lease };
                Draw(presenter).Dispose();
                using var pixels = lease.Pixels();
                Assert.True(AllCovered(pixels, defaultFill));
                Assert.Equal(2, lease.CanvasReads);
                Assert.Equal(1, lease.Disposals);
                Assert.Equal(0, presenter.MosaicRenderCount);
                Assert.Equal(0, presenter.GeneratedRenderCount);
            }
            finally
            {
                lease.Free();
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void GoodLease_DrawsThePattern_AndIsDisposedOnce(PatternKind _pattern)
        {
            var lease = new FakeLease(0, SKColors.Red);
            try
            {
                using var presenter = new MosaicPresenter { Model = NewArea(_pattern), Frames = GoodFrame(), LeaseSkia = _ => lease };
                Draw(presenter).Dispose();
                using var pixels = lease.Pixels();
                Assert.NotEqual(0, CountOther(pixels, SKColors.Red)); // drawn over the red
                Assert.False(AllCovered(pixels, defaultFill));
                Assert.Equal(1, lease.CanvasReads);
                Assert.Equal(1, lease.Disposals);
                Assert.Equal(_pattern == PatternKind.Mosaic ? 1 : 0, presenter.MosaicRenderCount);
                Assert.Equal(_pattern == PatternKind.Generated ? 1 : 0, presenter.GeneratedRenderCount);
            }
            finally
            {
                lease.Free();
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void NoSkiaLease_AvaloniaCoversTheArea(PatternKind _pattern)
        {
            using var presenter = new MosaicPresenter { Model = NewArea(_pattern, "#203040"), Frames = GoodFrame(), LeaseSkia = _ => null };
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, new SKColor(0x20, 0x30, 0x40)));
            Assert.Equal(0, presenter.MosaicRenderCount);
            Assert.Equal(0, presenter.GeneratedRenderCount);
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void LeaseWhoseCanvasAlwaysFails_AvaloniaCoversTheArea_AndTheLeaseIsDisposed(PatternKind _pattern)
        {
            var lease = new FakeLease(-1, SKColors.Red);
            try
            {
                using var log = new StringWriter();
                using var presenter = new MosaicPresenter { Model = NewArea(_pattern), Frames = GoodFrame(), LeaseSkia = _ => lease };
                SKBitmap bitmap;
                using (Log.CaptureTo(log))
                {
                    bitmap = Draw(presenter);
                }
                using (bitmap)
                {
                    Assert.True(AllCovered(bitmap, defaultFill));
                }
                Assert.Equal(2, lease.CanvasReads); // the pattern, then the cover through Skia
                Assert.Equal(1, lease.Disposals);
                Assert.Contains("pattern draw failed: canvas lost", log.ToString(), StringComparison.Ordinal);
                Assert.Contains("cover through Skia failed: canvas lost", log.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                lease.Free();
            }
        }

        [AvaloniaFact]
        public void Disposed_CoversWithTheFillColor_WithoutMakingImages()
        {
            // The window is closing: no new pattern image (it would leak past the retire list), but its last frames cover.
            var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic, "#0A0B0C"), Frames = GoodFrame() };
            Draw(presenter).Dispose();
            presenter.Dispose();
            Publish(presenter.Frames!, W, H);
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, new SKColor(0x0A, 0x0B, 0x0C)));
            Assert.Equal(1, presenter.MosaicRenderCount);
            Assert.Equal(0, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void Disposed_GeneratedPattern_IsCoveredToo()
        {
            var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
            presenter.Dispose();
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, defaultFill));
            Assert.Equal(0, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void FillPattern_IsNotAffected()
        {
            // The fill pattern never goes through Skia: a missing lease changes nothing.
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Fill, "#336699"), LeaseSkia = _ => throw new InvalidOperationException("not used") };
            using var bitmap = Draw(presenter);
            Assert.True(AllCovered(bitmap, new SKColor(0x33, 0x66, 0x99)));
        }

        [AvaloniaFact]
        public void DefaultLease_IsTheSkiaFeatureOfTheContext()
        {
            // Without a replacement the real Skia lease is used: the generated pattern is drawn, not the cover.
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
            Assert.NotNull(presenter.LeaseSkia);
            using var bitmap = Draw(presenter);
            Assert.Equal(1, presenter.GeneratedRenderCount);
            Assert.False(AllCovered(bitmap, defaultFill));
        }
    }
}
