using Avalonia.Headless.XUnit;
using Hider.Areas;
using Hider.Core;
using Hider.Core.Model;
using SkiaSharp;
using static Hider.App.Tests.Areas.CoverKit;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// When a pattern cannot be drawn the area is filled with its fill color, opaque, never left see-through (NFR-05): a draw
    /// that throws (also on a canvas that already holds other pixels), no Skia lease, a lease whose canvas fails, the window
    /// closing. The pattern itself is drawn as before once it can be.
    /// </summary>
    public class MosaicPresenterCoverTests
    {
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
            Assert.True(AllCovered(bitmap, DefaultFill));
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
            Assert.Equal(0, CountOther(bitmap, DefaultFill, 40, 30));
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
                Assert.True(AllCovered(covered, DefaultFill));
            }
            Publish(frames, W, H);
            using var bitmap = Draw(presenter);
            Assert.Equal(1, presenter.MosaicRenderCount);
            Assert.False(AllCovered(bitmap, DefaultFill));
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
        public void FailureOnACanvasHoldingOtherPixels_TheCoverReplacesThem(PatternKind _pattern)
        {
            // The lease's canvas already holds other pixels (red, standing in for whatever the render target had) and the
            // pattern draw fails at its first canvas read: the cover is drawn with BlendMode.Src, so it replaces every pixel
            // instead of blending over them.
            var lease = new FakeLease(1, SKColors.Red);
            try
            {
                using var presenter = new MosaicPresenter { Model = NewArea(_pattern), Frames = GoodFrame(), LeaseSkia = _ => lease };
                Draw(presenter).Dispose();
                using var pixels = lease.Pixels();
                Assert.True(AllCovered(pixels, DefaultFill));
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
                Assert.NotEqual(0, CountOther(pixels, SKColors.Red, W, H)); // drawn over the red
                Assert.False(AllCovered(pixels, DefaultFill));
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
                    Assert.True(AllCovered(bitmap, DefaultFill));
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
            Assert.True(AllCovered(bitmap, DefaultFill));
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
            Assert.False(AllCovered(bitmap, DefaultFill));
        }
    }
}
