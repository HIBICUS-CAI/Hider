using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// MosaicPresenter drawn on its own into a bitmap (spec §4.5, FR-42, P-1, NFR-05): nothing without a model or a size, fill,
    /// generated and mosaic patterns, the caches and when they are redrawn, unknown method ids, and draw failures.
    /// </summary>
    public class MosaicPresenterUnitTests
    {
        private const int W = 64, H = 48;

        private static Area NewArea(PatternKind _pattern, int _w = W, int _h = H)
        {
            var area = Area.CreateNew("0f3b8d2e", new PhysicalRect(10, 20, _w, _h), true);
            Hider.Core.Persistence.AreasFile.FillDefaults(area.Mosaic.Params, Hider.Core.Mosaic.MethodKind.Mosaic);
            Hider.Core.Persistence.AreasFile.FillDefaults(area.Generated.Params, Hider.Core.Mosaic.MethodKind.Generated);
            area.Pattern = _pattern;
            return area;
        }

        private static FrameExchange WithFrame(int _w = W, int _h = H)
        {
            var frames = new FrameExchange();
            Publish(frames, _w, _h);
            return frames;
        }

        private static void Publish(FrameExchange _frames, int _w, int _h)
        {
            _frames.Back.Resize(_w, _h);
            SyntheticImage.CreatePixels(_w, _h).AsSpan().CopyTo(_frames.Back.Bgra.Span);
            _frames.Publish();
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

        private static bool AllTransparent(SKBitmap _b)
        {
            for (int y = 0; y < _b.Height; y++)
            {
                for (int x = 0; x < _b.Width; x++)
                {
                    if (_b.GetPixel(x, y).Alpha != 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        [AvaloniaFact]
        public void NoModel_DrawsNothing()
        {
            using var presenter = new MosaicPresenter();
            Assert.Null(presenter.Model);
            Assert.Null(presenter.Frames);
            using var bitmap = Draw(presenter);
            Assert.True(AllTransparent(bitmap));
            Assert.Equal(0, presenter.GeneratedRenderCount);
            Assert.Equal(0, presenter.MosaicRenderCount);
        }

        [AvaloniaTheory]
        [InlineData(0, H)]
        [InlineData(W, 0)]
        [InlineData(0, 0)]
        public void NoSize_DrawsNothing(double _w, double _h)
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
            using var bitmap = Draw(presenter, _w, _h);
            Assert.True(AllTransparent(bitmap));
            Assert.Equal(0, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void Fill_IsThePlainColour_FollowingItsChanges()
        {
            var area = NewArea(PatternKind.Fill);
            area.Fill.Color = "#336699";
            using var presenter = new MosaicPresenter { Model = area };
            using (var first = Draw(presenter))
            {
                Assert.Equal(new SKColor(0x33, 0x66, 0x99, 0xFF), first.GetPixel(5, 5));
                Assert.Equal(new SKColor(0x33, 0x66, 0x99, 0xFF), first.GetPixel(W - 1, H - 1));
            }
            using (var same = Draw(presenter))
            {
                Assert.Equal(new SKColor(0x33, 0x66, 0x99, 0xFF), same.GetPixel(5, 5));
            }
            area.Fill.Color = "#CC0011";
            using var changed = Draw(presenter);
            Assert.Equal(new SKColor(0xCC, 0x00, 0x11, 0xFF), changed.GetPixel(5, 5));
            Assert.Equal(0, presenter.GeneratedRenderCount);
            Assert.Equal(0, presenter.MosaicRenderCount);
        }

        [AvaloniaFact]
        public void Generated_IsDrawnOnce_ThenCopied_AndAgainForANewSize()
        {
            var area = NewArea(PatternKind.Generated);
            using var presenter = new MosaicPresenter { Model = area };
            using (var first = Draw(presenter))
            {
                Assert.Equal(255, first.GetPixel(W / 2, H / 2).Alpha);
            }
            Assert.Equal(1, presenter.GeneratedRenderCount);
            Draw(presenter).Dispose();
            Assert.Equal(1, presenter.GeneratedRenderCount);
            area.Rect = area.Rect with { Width = W + 8 };
            Draw(presenter).Dispose();
            Assert.Equal(2, presenter.GeneratedRenderCount);
            Assert.Equal(0, presenter.MosaicRenderCount);
        }

        [AvaloniaFact]
        public void Mosaic_BeforeAnyFrame_CoversWithTheGeneratedPattern()
        {
            // NFR-05: until the first capture arrives the area stays covered.
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = new FrameExchange() };
            using var bitmap = Draw(presenter);
            Assert.Equal(255, bitmap.GetPixel(W / 2, H / 2).Alpha);
            Assert.Equal(1, presenter.GeneratedRenderCount);
            Assert.Equal(0, presenter.MosaicRenderCount);
        }

        [AvaloniaFact]
        public void Mosaic_WithoutAFrameExchange_CoversWithTheGeneratedPattern()
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic) };
            using var bitmap = Draw(presenter);
            Assert.Equal(255, bitmap.GetPixel(W / 2, H / 2).Alpha);
            Assert.Equal(1, presenter.GeneratedRenderCount);
            Assert.Equal(0, presenter.MosaicRenderCount);
        }

        [AvaloniaFact]
        public void Mosaic_FrameCleared_GoesBackToTheGeneratedPattern()
        {
            var frames = WithFrame();
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = frames };
            Draw(presenter).Dispose();
            Assert.Equal(1, presenter.MosaicRenderCount);
            frames.Clear();
            using var bitmap = Draw(presenter);
            Assert.Equal(255, bitmap.GetPixel(W / 2, H / 2).Alpha);
            Assert.Equal(1, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void Mosaic_IsRedrawnOnlyForANewFrameValuesOrPlace()
        {
            var area = NewArea(PatternKind.Mosaic);
            var frames = WithFrame();
            using var presenter = new MosaicPresenter { Model = area, Frames = frames };
            Draw(presenter).Dispose();
            Assert.Equal(1, presenter.MosaicRenderCount);
            Draw(presenter).Dispose();
            Assert.Equal(1, presenter.MosaicRenderCount); // a repaint copies the cached image

            Publish(frames, W, H);
            Draw(presenter).Dispose();
            Assert.Equal(2, presenter.MosaicRenderCount);

            area.Mosaic.ParamsFor("pixelate")["block"] = 4;
            Draw(presenter).Dispose();
            Assert.Equal(3, presenter.MosaicRenderCount);

            area.Mosaic.Common.Levels = 16;
            Draw(presenter).Dispose();
            Assert.Equal(4, presenter.MosaicRenderCount);

            area.Rect = area.Rect.Offset(3, 0); // the cells are anchored to the screen
            Draw(presenter).Dispose();
            Assert.Equal(5, presenter.MosaicRenderCount);

            area.Opacity = 0.5; // the fineness
            Draw(presenter).Dispose();
            Assert.Equal(6, presenter.MosaicRenderCount);

            Publish(frames, W + 4, H);
            Draw(presenter).Dispose();
            Assert.Equal(7, presenter.MosaicRenderCount);

            Draw(presenter).Dispose();
            Assert.Equal(7, presenter.MosaicRenderCount);
            Assert.Equal(0, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void Mosaic_UnknownMethod_IsDrawnWithTheDefaultMethod()
        {
            var known = NewArea(PatternKind.Mosaic);
            var unknown = NewArea(PatternKind.Mosaic);
            unknown.Mosaic.Method = "no-such-method";
            using var a = new MosaicPresenter { Model = known, Frames = WithFrame() };
            using var b = new MosaicPresenter { Model = unknown, Frames = WithFrame() };
            using var expected = Draw(a);
            using var actual = Draw(b);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
            Assert.Equal(1, b.MosaicRenderCount);
        }

        [AvaloniaFact]
        public void Generated_UnknownMethod_IsDrawnWithTheDefaultMethod()
        {
            var known = NewArea(PatternKind.Generated);
            var unknown = NewArea(PatternKind.Generated);
            unknown.Generated.Method = "no-such-pattern";
            using var a = new MosaicPresenter { Model = known };
            using var b = new MosaicPresenter { Model = unknown };
            using var expected = Draw(a);
            using var actual = Draw(b);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [AvaloniaFact]
        public void Mosaic_MethodOfTheOtherKind_IsDrawnWithTheDefaultMethod()
        {
            // A generated pattern's id stored as the mosaic method is no mosaic method.
            var known = NewArea(PatternKind.Mosaic);
            var wrongKind = NewArea(PatternKind.Mosaic);
            wrongKind.Mosaic.Method = wrongKind.Generated.Method;
            using var a = new MosaicPresenter { Model = known, Frames = WithFrame() };
            using var b = new MosaicPresenter { Model = wrongKind, Frames = WithFrame() };
            using var expected = Draw(a);
            using var actual = Draw(b);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [AvaloniaFact]
        public void DrawFailure_IsCaught_AndLeavesTheCachesAlone()
        {
            // A front frame without pixels (no capture makes one) cannot be drawn: the render thread carries on.
            var frames = new FrameExchange();
            frames.Back.Resize(0, 0);
            frames.Publish();
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = frames };
            var ex = Record.Exception(() => Draw(presenter).Dispose());
            Assert.Null(ex);
            Assert.Equal(0, presenter.MosaicRenderCount);
            Publish(frames, W, H); // the next good frame draws normally
            using var bitmap = Draw(presenter);
            Assert.Equal(1, presenter.MosaicRenderCount);
            Assert.Equal(255, bitmap.GetPixel(W / 2, H / 2).Alpha);
        }

        [AvaloniaFact]
        public void ReleaseCaches_DropsTheMosaicImage_SoTheNextFrameDrawsItAgain()
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = WithFrame() };
            Draw(presenter).Dispose();
            presenter.ReleaseCaches();
            presenter.ReleaseCaches(); // nothing left to release
            Draw(presenter).Dispose();
            Assert.Equal(2, presenter.MosaicRenderCount);
        }

        [AvaloniaFact]
        public void ReleaseCaches_BeforeAnyDraw_IsFine()
        {
            using var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Generated) };
            presenter.ReleaseCaches();
            Draw(presenter).Dispose();
            Assert.Equal(1, presenter.GeneratedRenderCount);
        }

        [AvaloniaFact]
        public void Disposed_DrawsNoMosaicOrPattern_AndDisposingTwiceIsFine()
        {
            // No new images once disposed; the area is covered with its fill color instead (MosaicPresenterCoverTests).
            var presenter = new MosaicPresenter { Model = NewArea(PatternKind.Mosaic), Frames = WithFrame() };
            Draw(presenter).Dispose();
            presenter.Dispose();
            presenter.Dispose();
            Publish(presenter.Frames!, W, H);
            using var bitmap = Draw(presenter);
            Assert.Equal(1, presenter.MosaicRenderCount);
            Assert.Equal(0, presenter.GeneratedRenderCount);
            Assert.False(AllTransparent(bitmap));
            Assert.Equal(new SKColor(0x68, 0x76, 0x8A), bitmap.GetPixel(W / 2, H / 2));
        }

        [AvaloniaFact]
        public void Retire_Defaults_AreTheSharedListAndSkiasOwnTest()
        {
            using var presenter = new MosaicPresenter();
            Assert.Same(MosaicPresenter.RetiredImages.Shared, presenter.Retired);
            using var raster = SKImage.Create(new SKImageInfo(4, 4));
            Assert.False(presenter.IsTextureBacked(raster));
        }

        /// <summary>A presenter whose cached images all count as GPU textures, retired into <paramref name="_retired"/>.</summary>
        private static MosaicPresenter AsGpu(PatternKind _pattern, MosaicPresenter.RetiredImages _retired, List<SKImage> _asked)
        {
            return new MosaicPresenter
            {
                Model = NewArea(_pattern),
                Frames = _pattern == PatternKind.Mosaic ? WithFrame() : null,
                Retired = _retired,
                IsTextureBacked = _i =>
                {
                    _asked.Add(_i);
                    return true;
                },
            };
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void Dispose_GpuImage_WaitsInTheRetireList_UntilTheNextDrawOfAnyArea(PatternKind _pattern)
        {
            // GPU textures are freed on the render thread (with the GrContext), never on the UI thread where Dispose runs
            // (crashes seen on Windows otherwise): the next draw of any area frees them.
            var retired = new MosaicPresenter.RetiredImages();
            var asked = new List<SKImage>();
            var closing = AsGpu(_pattern, retired, asked);
            Draw(closing).Dispose();
            Assert.Empty(asked);
            Assert.Equal(0, retired.Count);
            closing.Dispose();
            var image = Assert.Single(asked); // the one cached image (no frame: no mosaic; a frame: no generated pattern)
            Assert.Equal(1, retired.Count);
            Assert.NotEqual(IntPtr.Zero, image.Handle); // still alive on the UI thread

            using var other = new MosaicPresenter { Model = NewArea(PatternKind.Generated), Retired = retired };
            Draw(other).Dispose();
            Assert.Equal(0, retired.Count);
            Assert.Equal(IntPtr.Zero, image.Handle);
            Draw(other).Dispose(); // nothing left to free
            Assert.Equal(0, retired.Count);
        }

        [AvaloniaFact]
        public void ReleaseCaches_GpuImages_AreRetired_AndTheDisposedPresentersOwnDrawFreesThem()
        {
            var retired = new MosaicPresenter.RetiredImages();
            var asked = new List<SKImage>();
            using var presenter = AsGpu(PatternKind.Mosaic, retired, asked);
            presenter.Frames!.Clear();
            Draw(presenter).Dispose(); // no frame: the generated pattern
            Publish(presenter.Frames, W, H);
            Draw(presenter).Dispose(); // then the mosaic: both images are cached
            presenter.ReleaseCaches();
            Assert.Equal(2, asked.Count);
            Assert.Equal(2, retired.Count);
            Assert.All(asked, _i => Assert.NotEqual(IntPtr.Zero, _i.Handle));
            presenter.Dispose();
            Draw(presenter).Dispose(); // a closing window's last frame still frees the list first
            Assert.Equal(0, retired.Count);
            Assert.All(asked, _i => Assert.Equal(IntPtr.Zero, _i.Handle));
        }

        [AvaloniaFact]
        public void Dispose_RasterImage_IsFreedAtOnce_WithoutTheRetireList()
        {
            var retired = new MosaicPresenter.RetiredImages();
            var asked = new List<SKImage>();
            var presenter = new MosaicPresenter
            {
                Model = NewArea(PatternKind.Generated),
                Retired = retired,
                IsTextureBacked = _i =>
                {
                    asked.Add(_i);
                    return false;
                },
            };
            Draw(presenter).Dispose();
            presenter.Dispose();
            var image = Assert.Single(asked);
            Assert.Equal(0, retired.Count);
            Assert.Equal(IntPtr.Zero, image.Handle);
        }

        [AvaloniaFact]
        public void RetiredImages_FreeAll_DisposesEachOnce_AndEmptiesTheList()
        {
            var retired = new MosaicPresenter.RetiredImages();
            var a = SKImage.Create(new SKImageInfo(4, 4));
            var b = SKImage.Create(new SKImageInfo(2, 2));
            retired.Add(a);
            retired.Add(b);
            Assert.Equal(2, retired.Count);
            retired.FreeAll();
            Assert.Equal(0, retired.Count);
            Assert.Equal(IntPtr.Zero, a.Handle);
            Assert.Equal(IntPtr.Zero, b.Handle);
            retired.FreeAll();
            Assert.Equal(0, retired.Count);
        }

        [AvaloniaFact]
        public void DrawnArea_IsWhatThePointerHits()
        {
            // The custom draw operation answers hit tests for the presenter's whole rectangle.
            using var f = new AreaHarness();
            var area = f.Add(new PhysicalRect(100, 100, 200, 120), PatternKind.Generated);
            area.ExitEdit();
            area.Window.CaptureRenderedFrame()?.Dispose();
            var hit = area.Window.InputHitTest(new Point(100, 60));
            Assert.Same(area.Window.PatternView, hit);
        }
    }
}
